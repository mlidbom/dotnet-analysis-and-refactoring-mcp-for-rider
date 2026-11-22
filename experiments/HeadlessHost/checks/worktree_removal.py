"""Remove a disposable Git worktree while the integrated host's Roslyn worker is alive."""

from datetime import datetime
from contextlib import contextmanager
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
from urllib.error import URLError

import psutil

from .engine import CACHE, HEADLESS_PORT, HERE, ROOT, LiveEngine, request


FIXTURE = Path("tests/DotNetAnalysisAndRefactoringMcp.FixtureSolution")
SOLUTION_FILE = FIXTURE.name + ".slnx"
PATCHER = "Royder.Headless.RoslynPatcher.HeadlessSolutionProcessStartInfoPatcher"


def git(repository, *arguments):
    return subprocess.check_output(["git", "-C", repository, *arguments], cwd=ROOT, text=True, encoding="utf-8").strip()


class PrivateWorktree:
    def __init__(self, scratch):
        self.repository = scratch / "repository"
        self.path = scratch / "target"
        self.solution = self.path / SOLUTION_FILE
        self.repository.mkdir()
        git(self.repository, "init", "-q")
        git(self.repository, "config", "core.hooksPath", str(scratch / "no-hooks"))
        for name in git(ROOT, "ls-files", "-z", "--", FIXTURE.as_posix()).split("\0"):
            if name:
                target = self.repository / Path(name).relative_to(FIXTURE)
                target.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(ROOT / name, target)
        self._add_generator(scratch)
        (self.repository / ".gitignore").write_text("bin/\nobj/\n", encoding="utf-8")
        # Give the real startup index a measurable window on a fast machine.
        indexing_sources = self.repository / "Shapes" / "Indexing"
        indexing_sources.mkdir()
        for group in range(320):
            types = "".join(f"internal sealed class Item{group}_{i} {{ public int Value => {i}; }}\n" for i in range(16))
            (indexing_sources / f"Group{group}.cs").write_text("namespace Shapes.Indexing;\n" + types, encoding="utf-8")
        git(self.repository, "add", "-f", "--", ".")
        git(self.repository, "-c", "user.name=Royder fixture", "-c", "user.email=fixture@localhost",
            "-c", "commit.gpgSign=false", "commit", "-qm", "Private removal fixture")
        git(self.repository, "worktree", "add", "-q", "--detach", self.path, "HEAD")
        config = scratch / "NuGet.Config"
        config.write_text("<configuration><packageSources><clear /></packageSources></configuration>", encoding="utf-8")
        subprocess.run(["dotnet", "restore", self.solution, "--configfile", config, "--disable-build-servers",
                        "-p:NuGetAudit=false", "-v:q"], cwd=scratch, check=True)
        self.verify_target(scratch)
        self.gitfile = (self.path / ".git").read_bytes()

    def _add_generator(self, scratch):
        artifacts = scratch / "generator-build"
        project = HERE.parent / "HeadlessRoslynPatcher/FixtureChecks/FixtureChecks.csproj"
        subprocess.run(["dotnet", "build", project, "--artifacts-path", artifacts, "-c", "Debug", "--disable-build-servers",
                        "-p:UseSharedCompilation=false", "-v:q"],
                       cwd=scratch, check=True)
        analyzer = self.repository / "analyzers/FixtureChecks.dll"
        analyzer.parent.mkdir()
        shutil.copyfile(artifacts / "bin/FixtureChecks/debug/FixtureChecks.dll", analyzer)
        shapes = self.repository / "Shapes/Shapes.csproj"
        shapes.write_text(shapes.read_text(encoding="utf-8").replace("</Project>",
                          '<ItemGroup><Analyzer Include="../analyzers/FixtureChecks.dll" /></ItemGroup>\n</Project>'),
                          encoding="utf-8")
        (shapes.parent / "RemovalProbe.cs").write_text(
            "namespace Shapes;\ninternal static class RemovalProbe { internal static int RoundThreeAnalyzerTarget(int n) => RoundThreeGenerated.Twice(n); }\n",
            encoding="utf-8")

    def registered(self):
        records = git(self.repository, "worktree", "list", "--porcelain", "-z").split("\0")
        return any(Path(field.removeprefix("worktree ")).resolve() == self.path for field in records if field.startswith("worktree "))

    def verify_target(self, scratch):
        if (self.path.resolve() != scratch.resolve() / "target" or self.repository.resolve() != scratch.resolve() / "repository"
                or Path(git(self.path, "rev-parse", "--show-toplevel")).resolve() != self.path
                or Path(git(self.path, "rev-parse", "--path-format=absolute", "--git-common-dir")).resolve() != self.repository / ".git"
                or not self.registered() or git(self.path, "status", "--porcelain")):
            raise RuntimeError("Expected a clean, registered worktree of the private repository.")

    def remove_once(self, engine):
        if self.path.resolve() != self.repository.parent / "target" or (self.path / ".git").read_bytes() != self.gitfile:
            raise RuntimeError("The private worktree identity changed before removal.")
        started = time.time()
        try:
            result = subprocess.run(["git", "worktree", "remove", self.path], cwd=self.repository,
                                    stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=30)
        except subprocess.TimeoutExpired:
            engine.observe("plain git worktree remove timed out", True, started=started)
            raise
        finished = time.time()
        engine.observe("plain git worktree remove", {"started": started, "finished": finished,
                       "exit": result.returncode, "stdout": result.stdout, "stderr": result.stderr})
        if result.returncode or self.path.exists() or self.registered():
            raise RuntimeError(f"First git worktree remove failed or left the tree registered: {result.stderr}")
        return finished


def process_argument(process, *names):
    return any(Path(arg).name.lower() in names for arg in process.cmdline())


def roslyn_worker(launcher):
    for process in launcher.children():
        try:
            if process.name().lower() != "dotnet.exe" or not process_argument(process, "headlesshost.dll"):
                continue
            for child in process.children(recursive=True):
                if child.name().lower() in {"dotnet.exe", "jetbrains.roslyn.worker.exe"} and process_argument(
                        child, "jetbrains.roslyn.worker.exe", "jetbrains.roslyn.worker.dll"):
                    return process, child
        except psutil.NoSuchProcess:
            continue
    return None


def identity(worker):
    return {"pid": worker.pid, "started": worker.create_time(), "cwd": str(Path(worker.cwd()).resolve())}


@contextmanager
def owned_processes(evidence):
    processes = set()
    try:
        yield processes
    finally:
        error = sys.exception()
        _, survivors = psutil.wait_procs(processes, timeout=8)
        if survivors:
            # psutil verifies process creation time before kill, including for a reused PID.
            for process in survivors:
                try:
                    if process.is_running():
                        process.kill()
                except psutil.NoSuchProcess:
                    pass  # Exited between the wait and the kill.
            _, still_running = psutil.wait_procs(survivors, timeout=5)
            details = {"survivedShutdown": [p.pid for p in survivors], "stillRunning": [p.pid for p in still_running]}
            (evidence / "shutdown-failure.json").write_text(json.dumps(details), encoding="utf-8")
            if error is None:
                raise RuntimeError(f"Engine descendants survived normal shutdown: {details}")
            error.add_note(f"Engine shutdown also needed cleanup: {details}")


def setup_complete(engine, pid):
    return any("Setup Complete!" in log.read_text(encoding="utf-8", errors="replace")
               for log in (engine.cache / "logs/RoslynWorker").glob(f"*/{pid}_*.log"))


def await_phase(engine, worktree, indexing, owned):
    launcher = psutil.Process(engine.process.pid)
    deadline = time.monotonic() + 180
    while time.monotonic() < deadline:
        if engine.process.poll() is not None:
            raise RuntimeError(f"Host exited. See {engine.evidence / 'engine.log'}")
        if indexing and engine.indexed_line:
            raise RuntimeError("The host finished indexing before the running worker and active index could be observed.")
        workers = roslyn_worker(launcher)
        if workers:
            host, worker = workers
            owned.update(workers)
            ready = setup_complete(engine, worker.pid)
            if not indexing and not ready:
                time.sleep(.04)
                continue
            try:
                health = request(HEADLESS_PORT, "health", timeout=1)
            except (URLError, TimeoutError):
                health = {}
            for solution in health.get("solutions", []):
                if (Path(solution["path"]) == worktree.solution and "healthFailed" not in solution
                        and solution.get("indexing") is indexing):
                    if Path(host.cwd()).resolve() != engine.cache:
                        raise RuntimeError(f"Host PID {host.pid} has unexpected cwd: {host.cwd()}")
                    if host.environ().get("JETBRAINS_NO_CHECK_FOR_UPDATES") != "1":
                        raise RuntimeError("The engine's environment did not disable the JetBrains update check.")
                    if Path(worker.cwd()).resolve() != engine.cache / "temp/RoslynWorker":
                        raise RuntimeError(f"Worker PID {worker.pid} has unexpected cwd: {worker.cwd()}")
                    engine.observe("removal phase", solution, host=identity(host), roslyn=identity(worker),
                                   workerSetupComplete=ready)
                    return host, worker
        time.sleep(.04)
    raise TimeoutError("Did not observe the requested index state and a live Roslyn worker together.")


def check_removal(phase, request_zones=None, live_daemon=False, no_swea=False):
    indexing = phase == "indexing"
    evidence = CACHE / "evidence" / (datetime.now().strftime("%Y%m%d-%H%M%S-%f") + "-remove-" + phase)
    evidence.mkdir(parents=True)
    print("Evidence:", evidence, flush=True)
    with tempfile.TemporaryDirectory(prefix="remove-", dir=HERE / "obj") as owned:
        scratch = Path(owned).resolve()
        tree = PrivateWorktree(scratch)
        with owned_processes(evidence) as owned, LiveEngine(
            evidence, tree.solution, wait_for_ready=not indexing, cache=scratch / "engine-state",
            request_zones=request_zones, live_daemon=live_daemon, no_swea=no_swea
        ) as engine:
            host, worker = await_phase(engine, tree, indexing, owned)
            if not indexing:
                services = engine.services()
                if services["processStartInfoPatcher"] != PATCHER:
                    raise RuntimeError(f"Unexpected process patcher: {services}")
                reply = engine.call("find_usages", symbolName="RoundThreeGenerated.Twice")
                if "1 usages in 1 files" not in reply or "RemovalProbe.cs" not in reply:
                    raise RuntimeError(f"Unexpected usage query: {reply}")
                host, worker = await_phase(engine, tree, indexing, owned)
            before = identity(worker)
            finished = tree.remove_once(engine)
            after = identity(worker)
            if not host.is_running() or not worker.is_running() or after != before:
                raise RuntimeError("The engine and the original Roslyn worker must survive the removal.")
            time.sleep(.3)
            if tree.path.exists() or tree.registered():
                raise RuntimeError("The removed worktree reappeared.")
            engine.observe("worktree removed", True, phase=phase, roslynAfter=after, enginePid=host.pid)
        if not engine.indexed_line:
            raise RuntimeError("Could not compare Git's completion with the engine's INDEXED timestamp.")
        fields = dict(it.split("=", 1) for it in engine.indexed_line.split()[1:])
        indexed = datetime.fromisoformat(fields["started"]).timestamp() + float(fields["seconds"])
        correct_phase = indexing == (finished < indexed)
        exists, registered = tree.path.exists(), tree.registered()
        engine.observe("phase check after shutdown", {"indexed": indexed, "gitFinished": finished, "correctPhase": correct_phase,
                       "engineExited": True, "workerExited": True, "workerSetupCompletedBeforeExit": setup_complete(engine, worker.pid),
                       "worktreeExists": exists, "registered": registered})
        if not correct_phase or exists or registered:
            raise RuntimeError("The observed index timing or final worktree state did not match the requested phase.")
