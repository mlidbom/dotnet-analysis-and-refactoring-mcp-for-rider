"""Exercise the installed primary, two disposable Git worktrees and one private Rider JVM on Windows."""
import argparse
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
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
        print(f"{args[0]} exited {result.returncode}: {result.stdout[-3000:]}", file=sys.stderr, flush=True)
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


SETTINGS = ('<wpf:ResourceDictionary xml:space="preserve" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" '
            'xmlns:s="clr-namespace:System;assembly=mscorlib" xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation">'
            '{}</wpf:ResourceDictionary>')
SEVERITY = '<s:String x:Key="/Default/CodeInspection/Highlighting/InspectionSeverities/={}/@EntryIndexedValue">{}</s:String>'
INJECTION = "/Default/Environment/InjectedLayers/"


def everyday_settings(appdata, rider):
    """An everyday Rider's global settings in an APPDATA of their own, as Rider writes them: they raise InconsistentNaming
    to an error, and inject a personal file, by a path relative to the global file itself, which hides InconsistentNaming
    and UnusedField.Compiler. The global file applies over the file it injects."""
    product = json.loads((rider / "product-info.json").read_text(encoding="utf-8-sig"))["dataDirectoryName"]
    host = appdata / "JetBrains" / product / "resharper-host"
    personal = appdata / "JetBrains/Shared/vAny/Personal.DotSettings"
    for folder in (host, personal.parent):
        folder.mkdir(parents=True)
    personal.write_text(SETTINGS.format(SEVERITY.format("InconsistentNaming", "DO_NOT_SHOW") +
                                        SEVERITY.format("UnusedField_002ECompiler", "DO_NOT_SHOW")), encoding="utf-8")
    layer = "49E95C81559A714E98A9FFF6E0254068"
    (host / "GlobalSettingsStorage.DotSettings").write_text(SETTINGS.format(
        SEVERITY.format("InconsistentNaming", "ERROR") +
        f'<s:String x:Key="{INJECTION}FileInjectedLayer/={layer}/AbsolutePath/@EntryValue">{personal}</s:String>'
        f'<s:String x:Key="{INJECTION}FileInjectedLayer/={layer}/RelativePath/@EntryValue">..\\..\\..\\Shared\\vAny\\Personal.DotSettings</s:String>'
        f'<s:Boolean x:Key="{INJECTION}FileInjectedLayer/={layer}/@KeyIndexDefined">True</s:Boolean>'
        f'<s:Boolean x:Key="{INJECTION}InjectedLayerCustomization/=File{layer}/@KeyIndexDefined">True</s:Boolean>'
        f'<s:Double x:Key="{INJECTION}InjectedLayerCustomization/=File{layer}/RelativePriority/@EntryValue">1</s:Double>'), encoding="utf-8")
    return appdata


def verify(args):
    lab = Path(tempfile.mkdtemp(prefix="royder-on-demand-"))
    repo, first, second = [lab / name for name in ("repository", "first", "second")]
    if args.main_checkout:
        first = repo
    package = args.assets.resolve()
    manifest = json.loads((package / "package.json").read_text(encoding="utf-8-sig"))
    if args.catalog and json.loads(args.catalog.read_text(encoding="utf-8-sig"))["build"] != manifest["build"]:
        raise ValueError("The supplied catalog belongs to a different tools build; omit --catalog to capture a matching one.")
    primary = ["dotnet", str(args.primary.resolve())]
    config = lab / "primary.json"
    trusted_folder = lab / "trusted-folder"
    trusted_folder.mkdir()
    config.write_text(json.dumps(dict(
        port=args.port, repositories=[str(repo)], repositoryFolders=[str(trusted_folder)], catalogFile=str(lab / "catalog.json"),
        answerWithinSeconds=10 if args.check_timeout else 90, idleSeconds=60 if args.check_idle else 600,
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
    base_revision = command("git", "rev-parse", "HEAD", cwd=repo).strip()

    def checkout(path, incarnation=""):
        if path != repo:
            command("git", "worktree", "add", "--detach", path, base_revision, cwd=repo)
        probe = path / SOURCE
        marker = path.name + "Worktree" + incarnation
        probe.write_text(probe.read_text().replace("Twice(21);", f"Twice(21); // {marker}"))
        command("git", "commit", "-qam", "Distinguish this worktree's source", cwd=path)
        command("dotnet", "restore", path / SOLUTION, "--disable-build-servers", "-p:NuGetAudit=false")
    checkout(first)
    checkout(second)
    if args.main_checkout:
        project_settings = repo / ".idea" / ".idea.ManagedProbe" / ".idea"
        project_settings.mkdir(parents=True)
        (project_settings / "workspace.xml").write_text(
            '<project version="4"><component name="PropertiesComponent">'
            '<property name="pilot.personal.setting" value="remember-me"/></component></project>')
        (project_settings / "vcs.xml").write_text(
            '<project version="4"><component name="VcsDirectoryMappings">'
            '<mapping directory="$PROJECT_DIR$" vcs="Git"/></component></project>')
        personal_layer = repo / (SOLUTION + ".DotSettings.user")
        personal_layer.write_text('<wpf:ResourceDictionary xmlns:wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation" />')
        def idea_files():
            return {str(file.relative_to(repo)): (hashlib.sha256(file.read_bytes()).hexdigest(), file.stat().st_mtime_ns)
                    for file in [personal_layer, *(repo / ".idea").rglob("*")] if file.is_file()}
        before_settings = idea_files()
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
        # The primary answers a call to a solution still opening within its deadline, saying so: retry it, as an agent
        # does. Four minutes covers a cold open on an idle machine many times over.
        end = time.monotonic() + 240
        while True:
            result = request("tools/call", dict(name=name, arguments=args))
            if not (result.get("isError") and "still opening" in result["content"][0]["text"]) or time.monotonic() > end:
                break
            time.sleep(5)
        assert not result.get("isError"), result
        return result["content"][0]["text"]

    def listing():
        return {Path(row["path"]) for row in json.loads(tool("list_solutions", {}))["solutions"]}

    def generated(tree, incarnation=""):
        # First tool call names a file and a use of the generated method. A false ready signal fails this assertion.
        line = (tree / SOURCE).read_text().splitlines()[3]
        answer = tool("find_usages", dict(filePath=str(tree / SOURCE), line=4, column=line.index("Twice")+1))
        # Since 2026-11-26 a generated file is named by its generator and hint name, as Rider holds it with no path.
        declared = "declared: [generated by FixtureGenerator] RoundThreeGenerated.g.cs"
        assert declared in answer and "1 usage" in answer.lower() and tree.name + "Worktree" + incarnation in answer, answer
        return answer

    profile = lab / "profile"
    def generation():
        current = Path(json.loads((profile/"current.json").read_text(encoding="utf-8-sig")))
        environment = json.loads((current/"plan.json").read_text(encoding="utf-8-sig"))["environment"]
        assert environment.get("DOTNET_ADD_GLOBAL_TOOLS_TO_PATH") == "false", "A private SDK home must opt out of user PATH registration."
        state = json.loads((current/"state.json").read_text(encoding="utf-8-sig"))
        return current, state["hostProcess"]["pid"]

    def alive(pid):
        listed = subprocess.run(["tasklist", "/FI", f"PID eq {pid}", "/NH", "/FO", "CSV"], capture_output=True, text=True).stdout
        return f'"{pid}"' in listed

    def closed(solution):
        # close_worktree answers Still closing by its deadline, and Closed only once the solution's backend has exited:
        # on 2026-11-24 it answered nothing within the client's 60 seconds, of closes that had freed the folder.
        key = hashlib.sha256(str(solution).upper().encode()).hexdigest().upper()
        identity = generation()[0] / ("opened-" + key + ".json")
        backend = json.loads(identity.read_text(encoding="utf-8-sig"))["Process"]["Pid"] if identity.exists() else None
        end = time.monotonic() + 240
        while (text := tool("close_worktree", dict(solutionPath=str(solution)))).startswith("Still closing") and time.monotonic() < end:
            time.sleep(2)
        assert text.startswith("Closed") or text.startswith("Nothing to close"), text
        assert backend is None or not alive(backend), f"close_worktree answered {text!r} while backend {backend} still ran"
        return text

    environment = None
    if args.check_global_settings:
        # The primary's private Rider takes the everyday Rider's global settings from the APPDATA it runs with.
        environment = dict(os.environ, APPDATA=str(everyday_settings(lab / "appdata", args.rider)))
        (first / "SettingsProbe.cs").write_text("namespace Consumers;\npublic class SettingsProbe\n{\n   int unused_field;\n}\n")
    log = (lab / "primary.log").open("w", encoding="utf8")
    proc = subprocess.Popen(primary + ["serve", str(config)], stdout=log, stderr=subprocess.STDOUT, env=environment)
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
        # Until 2026-11-26 a file in a repository the settings don't name got only "Path is outside open solutions".
        stray = lab / "untrusted-repository"
        command("git", "init", "-q", stray)
        (stray / "Stray.cs").write_text("namespace Strays;\npublic class Stray;\n")
        refused = request("tools/call", dict(name="get_diagnostics", arguments=dict(filePath=str(stray / "Stray.cs"))))
        assert refused.get("isError") and "is in none of them: " + str(repo) in refused["content"][0]["text"], refused
        # Since 2026-11-26 a repository cloned into a folder the settings trust is trusted from the next call. With no
        # solution in it, the call gets past the trust and is refused by the solution lookup.
        late = trusted_folder / "late-repository"
        command("git", "init", "-q", late)
        (late / "Late.cs").write_text("namespace Lates;\npublic class Late;\n")
        trusted = request("tools/call", dict(name="get_diagnostics", arguments=dict(filePath=str(late / "Late.cs"))))
        assert trusted.get("isError") and f"Worktree '{late}' contains" in trusted["content"][0]["text"], trusted
        if args.check_timeout:
            source = first / SOURCE
            original = source.read_bytes()
            answer = request("tools/call", dict(name="rename_symbol", arguments=dict(
                filePath=str(source), line=2, column=14, newName="RenameMustNeverRun", dryRun=False)))
            assert answer.get("isError") and "still opening" in answer["content"][0]["text"], answer
            # Since 2026-11-24 it says how long the warm-up has run and what it waits for.
            assert "waits for" in answer["content"][0]["text"], answer
            print("Still opening:", answer["content"][0]["text"], flush=True)
            wait_until(lambda: first/SOLUTION in listing(), 150, "warm-up after the edit timed out")
            generated(first)
            assert source.read_bytes() == original and source.exists() and not (first/"RenameMustNeverRun.cs").exists()
            print("PASS: timed-out edit was never dispatched; its worktree still warmed up", flush=True)
            return lab
        if args.check_solution_edit:
            generated(first)
            # On 2026-11-24 the private Rider reloaded a solution that had gained a project with the projects the
            # edit touched only, and dropped the rest.
            other = first / "OtherProbe"
            other.mkdir()
            (other / "OtherProbe.csproj").write_text(
                '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
            (other / "Other.cs").write_text("namespace Others;\npublic sealed class Other;\n")
            command("dotnet", "restore", other / "OtherProbe.csproj", "--disable-build-servers", "-p:NuGetAudit=false")
            solution = first / SOLUTION
            solution.write_text(solution.read_text().replace("</Solution>", '<Project Path="OtherProbe/OtherProbe.csproj" /></Solution>'))
            def projects():
                answer = request("tools/call", dict(name="get_solution_structure", arguments=dict(solutionName=str(solution))))
                text = answer["content"][0]["text"]
                return {line[1:line.index("]")] for line in text.splitlines() if line.startswith("[")} if not answer.get("isError") else text
            seen = {}
            def both():
                seen["last"] = projects()
                return seen["last"] == {"ManagedProbe", "OtherProbe"}
            try:
                wait_until(both, 150, "both projects after the solution file gained one")
            except AssertionError as timeout:
                raise AssertionError(f"{timeout}. Royder listed last: {seen.get('last')}") from None
            print("PASS: a project added to the solution file joins the others", flush=True)
            return lab
        if args.check_unregistered_close:
            generated(first)
            # On 2026-11-24 git worktree remove unregistered a worktree but left its folder, which Rider's backend held,
            # and close_worktree refused the solution as in no trusted checkout.
            shutil.rmtree(repo / ".git" / "worktrees" / first.name)
            answer = request("tools/call", dict(name="close_worktree", arguments=dict(solutionPath=str(first / SOLUTION))))
            text = answer["content"][0]["text"]
            assert not answer.get("isError") and text.startswith(("Closed", "Nothing to close", "Still closing")), answer
            wait_until(lambda: not listing(), 100, "the unregistered worktree's project to close")
            shutil.rmtree(first)
            print(f"PASS: close_worktree let go of a worktree Git no longer lists: {text}", flush=True)
            return lab
        if args.check_global_settings:
            # Until 2026-11-25 the private Rider had none of the everyday Rider's global settings, nor the personal file
            # they inject: agents got warnings in its worktrees that Magnus's Rider lowers or hides.
            answer = tool("get_diagnostics", dict(filePath=str(first / "SettingsProbe.cs"), minSeverity="hint"))
            assert "error InconsistentNaming" in answer and "UnusedField.Compiler" not in answer, answer
            print("PASS: the private Rider applies the everyday global settings over the file they inject", flush=True)
            return lab
        previous = set((profile/"runs").glob("*"))
        with concurrent.futures.ThreadPoolExecutor(2) as pool:
            assert len(list(pool.map(generated, [first, first]))) == 2
        assert listing() == {first/SOLUTION}
        first_generation = generation()
        assert set((profile/"runs").glob("*")) - previous == {first_generation[0]}
        if args.main_checkout:
            # Git can replace both HEAD and the index during ordinary work; this is still the same checkout.
            command("git", "switch", "-qc", "continued-work", cwd=repo)
            command("git", "commit", "--allow-empty", "-qm", "Advance the current branch", cwd=repo)
            generated(repo)
            # Editing source remains functional even when the checkout's IntelliJ state is private.
            for old_name, new_name in [("RoundThreeAnalyzerTarget", "EditedFromHeadless"), ("EditedFromHeadless", "RoundThreeAnalyzerTarget")]:
                source = repo / SOURCE
                line = source.read_text().splitlines()[3]
                tool("rename_symbol", dict(filePath=str(source), line=4, column=line.index(old_name)+1, newName=new_name, dryRun=False))
                assert new_name in source.read_text() and old_name not in source.read_text()
            rejected = subprocess.run(primary + ["remove-worktree", str(config), str(repo)], capture_output=True, text=True)
            assert rejected.returncode and "main checkout" in rejected.stderr + rejected.stdout, rejected
            assert generation() == first_generation and listing() == {repo/SOLUTION}
            untrusted = lab / "other-repository"
            command("git", "init", "-q", untrusted)
            (untrusted / SOLUTION).write_text("<Solution/>")
            refused = request("tools/call", dict(name="warm_worktree", arguments=dict(solutionPath=str(untrusted/SOLUTION))))
            assert refused.get("isError") and listing() == {repo/SOLUTION}, refused
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
            assert closed(first/SOLUTION).startswith("Closed")
        assert not listing()
        if args.main_checkout:
            after_settings = idea_files()
            (lab / "main-settings-comparison.json").write_text(json.dumps(dict(before=before_settings, after=after_settings), indent=2))
            assert after_settings == before_settings, "The private host changed the main checkout's .idea settings"
        print("PASS: cold catalog, concurrent first call, generated symbols, shared JVM worktrees, both removal orders and final close", flush=True)
    finally:
        try:
            if listening and proc.poll() is None:
                for solution in listing():
                    closed(solution)
        finally:
            proc.terminate()
            proc.wait(timeout=20)
            log.close()
            for path in [second, first]:
                if (path / ".git").is_file():
                    command("git", "worktree", "remove", "--force", path, cwd=repo)
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
    parser.add_argument("--check-solution-edit", action="store_true", help="Verify a project added to an open solution's file joins the others")
    parser.add_argument("--check-unregistered-close", action="store_true", help="Verify close_worktree lets go of a worktree Git no longer lists")
    parser.add_argument("--check-global-settings", action="store_true", help="Verify the private Rider applies the everyday Rider's global settings and the files they inject")
    parser.add_argument("--main-checkout", action="store_true", help="Start with the trusted main checkout; verify it can serve tools but cannot be removed")
    verify(parser.parse_args())
