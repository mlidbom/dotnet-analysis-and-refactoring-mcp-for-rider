import os
import subprocess
import tempfile
from contextlib import contextmanager
from pathlib import Path
from uuid import uuid4

from .engine import HERE, ROOT


FIXTURE_PROJECT = HERE / "checks/fixture/LiveProbe.csproj"
DEVELOPMENT_ENVIRONMENT = ROOT / "tools/DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment"
PAIRED_PROJECT = DEVELOPMENT_ENVIRONMENT / (DEVELOPMENT_ENVIRONMENT.name + ".csproj")
TYPE = "LiveRoyderProbeSubject"
AFTER = "LiveRoyderProbeChanged"
IMPORT = "LiveRoyderProbeImport"
BEFORE_METHOD = "OriginalProbeMethod"
AFTER_METHOD = "RenamedProbeMethod"


class ProbeFiles:
    """Publish a compilable set of sources as one directory and withdraw it before Git restoration."""

    def __init__(self, check, evidence, project):
        self.check = check
        self.evidence = evidence
        self.project = project
        self.paired = project == PAIRED_PROJECT
        suffix = "_" + uuid4().hex[:12]
        self.folder = project.parent / ("HeadlessLiveProbe" + suffix)
        self.subject = TYPE + suffix if self.paired else TYPE
        self.changed_subject = AFTER + suffix if self.paired else AFTER
        self.import_name = IMPORT + suffix if self.paired else IMPORT
        self.namespace = project.stem + "." + self.folder.name
        self.source = self.folder / (self.subject + ".cs")
        self.moved = self.folder / "Destination" / self.source.name
        self.caller = self.folder / "ProbeCaller.cs"

    def write_type(self, path, name=None, body="", namespace=None):
        path.parent.mkdir(exist_ok=True)
        path.write_text(f"namespace {namespace or self.namespace};\n\ninternal sealed class {name or self.subject}\n{{\n{body}}}\n",
                        encoding="utf-8")

    def _prepare(self, staged):
        source = staged / self.source.name
        caller = staged / self.caller.name
        if self.check == "add":
            return
        self.write_type(source)
        if self.check == "rename":
            self.write_type(source, body=f"   internal int {BEFORE_METHOD}() => 7;\n")
            self.write_type(caller, "ProbeCaller", body=f"   int Call() => new {self.subject}().{BEFORE_METHOD}();\n")
        if self.check == "fix-usings":
            self.write_type(source, body=f"#if ROYDER_HEADLESS_IMPORT_PROBE\n"
                                         f"   internal {self.import_name} Create() => new();\n"
                                         f"#else\n   internal Dependency.{self.import_name} Create() => new();\n#endif\n")
            self.write_type(staged / "Dependency" / (self.import_name + ".cs"), self.import_name, namespace=self.namespace + ".Dependency")
        if self.check in {"move-file", "move-type"}:
            self.write_type(staged / "Destination" / "Anchor.cs", "LiveRoyderProbeAnchor",
                            namespace=self.namespace + ".Destination")
        if self.check == "move-type":
            self.write_type(caller, "ProbeCaller", body=f"   internal {self.subject} Create() => new();\n")

    @contextmanager
    def installed(self):
        with tempfile.TemporaryDirectory(dir=self.evidence) as scratch:
            staged = Path(scratch) / "sources"
            staged.mkdir()
            self._prepare(staged)
            baseline = {p.relative_to(staged): p.read_bytes() for p in staged.rglob("*.cs")}
            objects = Path(scratch) / "objects"
            objects.mkdir()
            env = {**os.environ, "GIT_INDEX_FILE": str(Path(scratch) / "index"), "GIT_OBJECT_DIRECTORY": str(objects)}
            git_dir = subprocess.check_output(["git", "-C", ROOT, "rev-parse", "--absolute-git-dir"], text=True).strip()

            def git(*args):
                subprocess.run(["git", "--git-dir", git_dir, "--work-tree", staged, "-c", "core.autocrlf=false", *args],
                               cwd=staged, env=env, check=True)

            git("read-tree", "--empty")
            if baseline:
                git("add", "-f", "--", *baseline)
            staged.rename(self.folder)
            try:
                if self.paired:
                    self._build(Path(scratch) / "build")
                yield self
                if self.paired:
                    self._build(Path(scratch) / "build")
            finally:
                self.folder.rename(staged)
                if baseline:
                    git("restore", "--worktree", "--", *baseline)
                    if any((staged / name).read_bytes() != data for name, data in baseline.items()):
                        raise RuntimeError("Git did not restore the probe's original bytes.")
                print("Removed probe and verified Git restoration:", self.folder, flush=True)

    def _build(self, artifacts):
        subprocess.run(["dotnet", "build", self.project, "--artifacts-path", artifacts,
                        "--nologo", "-v:q"], check=True)
