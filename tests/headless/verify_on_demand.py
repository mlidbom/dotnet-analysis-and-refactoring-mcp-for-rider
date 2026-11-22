"""Exercise the installed primary, two disposable Git worktrees and one private Rider JVM on Windows."""
import argparse
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[2]
HTTP = urllib.request.build_opener(urllib.request.ProxyHandler({}))
PROJECT = "ManagedProbe.csproj"
SOURCE = "Caller.cs"
SOLUTION = "ManagedProbe.slnx"


def command(*args, cwd=None):
    result = subprocess.run(list(map(str, args)), cwd=cwd, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if result.returncode:
        raise RuntimeError(f"{args[0]} exited {result.returncode}: {result.stdout[-3000:]}")
    return result.stdout


def wait_until(observation, seconds, description):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        try:
            if value := observation():
                return value
        except OSError:
            pass
        time.sleep(.25)
    raise AssertionError("Timed out waiting for " + description)


def verify(args):
    lab = Path(tempfile.mkdtemp(prefix="royder-on-demand-"))
    repo, first, second = [lab / name for name in ("repository", "first", "second")]
    package = args.assets.resolve()
    manifest = json.loads((package / "package.json").read_text(encoding="utf-8-sig"))
    if args.catalog and json.loads(args.catalog.read_text(encoding="utf-8-sig"))["build"] != manifest["build"]:
        raise ValueError("The supplied catalog belongs to a different tools build; omit --catalog to capture a matching one.")
    primary = ["dotnet", str(args.primary.resolve())]
    config = lab / "primary.json"
    config.write_text(json.dumps(dict(
        port=args.port, repositories=[str(repo)], catalogFile=str(lab / "catalog.json"),
        firstCallWaitSeconds=1 if args.check_timeout else 90, idleSeconds=60 if args.check_idle else 600,
        rider=dict(profileDirectory=str(lab / "profile"), riderHome=str(args.rider), assetsDirectory=str(package),
                   toolsBuild=manifest["build"], remoteDevPort=args.remote_port, backendMcpPort=args.backend_port)
    )), encoding="utf8")
    print("Private verification files:", lab, flush=True)
    repo.mkdir()
    command("git", "init", "-q", cwd=repo)
    command("git", "config", "user.name", "Royder specification", cwd=repo)
    command("git", "config", "user.email", "royder@localhost", cwd=repo)
    (repo / ".gitignore").write_text("bin/\nobj/\n.idea/\n")
    analyzer = lab / "generator"
    analyzer.mkdir()
    for source in ("FixtureGenerator.cs", "FixtureAnalyzer.cs"):
        shutil.copyfile(ROOT / "experiments/HeadlessRoslynPatcher/FixtureChecks" / source, analyzer / source)
    (analyzer / "Fixture.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>'
        '<ItemGroup><Reference Include="$(MSBuildToolsPath)/Roslyn/bincore/Microsoft.CodeAnalysis.dll;'
        '$(MSBuildToolsPath)/Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll" Private="false"/></ItemGroup></Project>')
    command("dotnet", "build", analyzer / "Fixture.csproj", "-c", "Release", "--disable-build-servers", "-p:NuGetAudit=false")
    shutil.copyfile(analyzer / "bin/Release/net10.0/Fixture.dll", repo / "Fixture.dll")
    (repo / PROJECT).write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>'
        '<ItemGroup><Analyzer Include="Fixture.dll" /></ItemGroup></Project>')
    (repo / SOLUTION).write_text('<Solution><Project Path="ManagedProbe.csproj" /></Solution>')
    (repo / SOURCE).write_text('namespace Consumers;\npublic class Caller\n{\n   public int RoundThreeAnalyzerTarget() => Shapes.RoundThreeGenerated.Twice(21);\n}\n')
    command("git", "add", ".", cwd=repo)
    command("git", "commit", "-qm", "Prepare generator fixture", cwd=repo)

    def checkout(path, incarnation=""):
        command("git", "worktree", "add", "--detach", path, "HEAD", cwd=repo)
        probe = path / SOURCE
        marker = path.name + "Worktree" + incarnation
        probe.write_text(probe.read_text().replace("Twice(21);", f"Twice(21); // {marker}"))
        command("git", "commit", "-qam", "Distinguish this worktree's source", cwd=path)
        command("dotnet", "restore", path / SOLUTION, "--disable-build-servers", "-p:NuGetAudit=false")
    checkout(first)
    checkout(second)
    if args.catalog:
        shutil.copyfile(args.catalog, lab / "catalog.json")
    elif args.capture_port:
        command(*primary, "capture-catalog", args.capture_port, manifest["build"], lab / "catalog.json")
    else:
        command(*primary, "prepare-catalog", config, first / SOLUTION)

    url = f"http://127.0.0.1:{args.port}"
    def request(method, params=None):
        data = json.dumps(dict(jsonrpc="2.0", id=1, method=method, params=params or {})).encode()
        req = urllib.request.Request(url, data=data, headers={"Content-Type":"application/json"})
        response = json.load(HTTP.open(req, timeout=145))
        if "error" in response:
            raise AssertionError(response["error"])
        return response["result"]

    def tool(name, args):
        result = request("tools/call", dict(name=name, arguments=args))
        assert not result.get("isError"), result
        return result["content"][0]["text"]

    def listing():
        return {Path(row["path"]) for row in json.loads(tool("list_solutions", {}))["solutions"]}

    def generated(tree, incarnation=""):
        # First tool call names a file and a use of the generated method. A false ready signal fails this assertion.
        line = (tree / SOURCE).read_text().splitlines()[3]
        answer = tool("find_usages", dict(filePath=str(tree / SOURCE), line=4, column=line.index("Twice")+1))
        assert "declared: [generated]" in answer and "1 usage" in answer.lower() and tree.name + "Worktree" + incarnation in answer, answer
        return answer

    profile = lab / "profile"
    def generation():
        current = Path(json.loads((profile/"current.json").read_text(encoding="utf-8-sig")))
        state = json.loads((current/"state.json").read_text(encoding="utf-8-sig"))
        return current, state["hostProcess"]["pid"]

    log = (lab / "primary.log").open("w", encoding="utf8")
    proc = subprocess.Popen(primary + ["serve", str(config)], stdout=log, stderr=subprocess.STDOUT)
    listening = False
    def healthy():
        if proc.poll() is not None:
            raise AssertionError("Primary exited during startup: " + (lab / "primary.log").read_text(errors="replace")[-1200:])
        return HTTP.open(url+"/health", timeout=2).status == 200

    try:
        wait_until(healthy, 45, "the standalone listener")
        listening = True
        assert not listing()
        names = {tool["name"] for tool in request("tools/list")["tools"]}
        assert {"find_usages", "warm_worktree", "close_worktree"} <= names
        if args.check_timeout:
            source = first / SOURCE
            original = source.read_bytes()
            answer = request("tools/call", dict(name="rename_symbol", arguments=dict(
                filePath=str(source), line=2, column=14, newName="RenameMustNeverRun", dryRun=False)))
            assert answer.get("isError") and "still opening" in answer["content"][0]["text"], answer
            wait_until(lambda: first/SOLUTION in listing(), 150, "warm-up after the edit timed out")
            generated(first)
            assert source.read_bytes() == original and source.exists() and not (first/"RenameMustNeverRun.cs").exists()
            print("PASS: timed-out edit was never dispatched; its worktree still warmed up", flush=True)
            return lab
        previous = set((profile/"runs").glob("*"))
        with concurrent.futures.ThreadPoolExecutor(2) as pool:
            assert len(list(pool.map(generated, [first, first]))) == 2
        assert listing() == {first/SOLUTION}
        first_generation = generation()
        assert set((profile/"runs").glob("*")) - previous == {first_generation[0]}
        if args.check_rejected_open:
            # Git names this solution in B. Rider rejects its nonphysical path before opening a project.
            alias = second / "rejected-junction"
            physical = lab / "physical-solution"
            physical.mkdir()
            solution_name = "Rejected.slnx"
            shutil.copyfile(second / SOLUTION, physical / solution_name)
            subprocess.run(["pwsh", "-NoProfile", "-Command",
                            "$null=New-Item -ErrorAction Stop -ItemType Junction -Path $env:CHECK_ALIAS -Target $env:CHECK_TARGET"],
                           env={**os.environ, "CHECK_ALIAS": str(alias), "CHECK_TARGET": str(physical)}, check=True)
            relative = f"{alias.name}/{solution_name}"
            try:
                blob = command("git", "rev-parse", "HEAD:" + SOLUTION, cwd=second).strip()
                command("git", "update-index", "--add", "--cacheinfo", f"100644,{blob},{relative}", cwd=second)
                denied = request("tools/call", dict(name="warm_worktree", arguments=dict(solutionPath=str(alias/solution_name))))
                assert denied.get("isError") and "HTTP 400" in denied["content"][0]["text"], denied
                records = json.loads((profile/"worktrees.json").read_text(encoding="utf-8-sig"))
                assert [Path(it["Solution"]) for it in records] == [first/SOLUTION], records
                assert listing() == {first/SOLUTION} and generation() == first_generation
                generated(first)
            finally:
                command("git", "update-index", "--force-remove", "--", relative, cwd=second)
                assert alias.is_junction() and alias.resolve() == physical.resolve()
                alias.rmdir()
            print("PASS: rejected B left the journal; A still serves in its original JVM", flush=True)
        generated(second)
        assert generation() == first_generation
        assert listing() == {first/SOLUTION, second/SOLUTION}
        # The JVM registered B, but its open reply was lost before the client persisted the ETag.
        # The durable request ID must recover that original project even after Git removes its files.
        key = hashlib.sha256(str(second/SOLUTION).upper().encode()).hexdigest().upper()
        (first_generation[0] / ("project-" + key + ".etag")).unlink()
        command("git", "worktree", "remove", second, cwd=repo)
        wait_until(lambda: listing() == {first/SOLUTION}, 100, "external removal to close only the old project")
        generated(first)
        assert not second.exists(), "External removal recreated the worktree"
        checkout(second)
        generated(second)
        if args.check_replacement:
            command("git", "worktree", "remove", second, cwd=repo)
            checkout(second, "Replacement")
            generated(second, "Replacement")
            assert not command("git", "status", "--porcelain", cwd=second)
        command(*primary, "remove-worktree", config, second)
        assert not second.exists()
        assert listing() == {first/SOLUTION}
        assert generation() == first_generation
        generated(first)
        if args.check_idle:
            wait_until(lambda: not listing(), 100, "idle eviction")
            wait_until(lambda: json.loads((first_generation[0]/"state.json").read_text(encoding="utf-8-sig"))["phase"]=="Stopped",
                       90, "last idle project to release the JVM")
        else:
            tool("close_worktree", dict(solutionPath=str(first/SOLUTION)))
        assert not listing()
        print("PASS: cold catalog, concurrent first call, generated symbols, shared JVM worktrees, both removal orders and final close", flush=True)
    finally:
        try:
            if listening and proc.poll() is None:
                for solution in listing():
                    tool("close_worktree", dict(solutionPath=str(solution)))
        finally:
            proc.terminate()
            proc.wait(timeout=20)
            log.close()
            for path in [second, first]:
                if (path / ".git").is_file():
                    command("git", "worktree", "remove", path, cwd=repo)
    return lab


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--assets", type=Path, required=True)
    parser.add_argument("--primary", type=Path, default=ROOT/"src/Royder.Primary/bin/Release/net10.0/Royder.Primary.dll")
    parser.add_argument("--rider", type=Path, default=Path(os.environ.get("LOCALAPPDATA",""))/"Programs/Rider")
    parser.add_argument("--port", type=int, default=24930)
    parser.add_argument("--remote-port", type=int, default=24931)
    parser.add_argument("--backend-port", type=int, default=24941)
    parser.add_argument("--capture-port", type=int)
    parser.add_argument("--catalog", type=Path, help="Previously captured catalog for this exact package build")
    parser.add_argument("--check-replacement", action="store_true", help="Immediately reuse an externally removed worktree path")
    parser.add_argument("--check-idle", action="store_true", help="Wait for the final idle solution and JVM to stop")
    parser.add_argument("--check-timeout", action="store_true", help="Verify a timed-out cold edit never executes")
    parser.add_argument("--check-rejected-open", action="store_true", help="Verify a rejected second solution clears quarantine while the first stays ready")
    verify(parser.parse_args())
