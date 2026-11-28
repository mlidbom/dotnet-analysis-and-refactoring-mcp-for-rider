"""Cold-open integration checks for Royder /health. Requires Windows, PowerShell 7 and the installed Rider."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import time
import urllib.error
import urllib.request
from xml.sax.saxutils import escape


REPOSITORY = Path(__file__).resolve().parents[2]
HARNESS = REPOSITORY / "src/Royder.Primary/Hosting/Launcher/RemoteDevHost.ps1"
HOST = "DotNetAnalysisAndRefactoringMcp"
TOOLS = HOST + ".Tools"
REAL_PROJECT = HOST + ".DevelopmentEnvironment"
FIXTURES = REPOSITORY / "experiments/HeadlessRoslynPatcher/FixtureChecks"
HTTP = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def dotnet(*args, env):
    subprocess.run(["dotnet", *map(str, args), "--disable-build-servers", "-m:1",
                    "-p:NuGetAudit=false", "-p:UseSharedCompilation=false"], env=env, check=True)


def stage_tools(root, rider, env):
    dotnet("build", REPOSITORY / "src" / TOOLS, "-c", "Release", env=env)
    output = REPOSITORY / "src" / TOOLS / "bin/Release/net472"
    host_output = REPOSITORY / "src" / HOST / "bin/Release/net472"
    identity = hashlib.sha256((host_output / (HOST + ".dll")).read_bytes()).hexdigest()
    settings = json.loads((rider / "product-info.json").read_text(encoding="utf-8-sig"))["dataDirectoryName"]
    plugin = Path("JetBrains") / settings / "plugins" / HOST
    private_plugin = root / "roaming-input" / plugin
    shutil.copytree(Path(os.environ["APPDATA"]) / plugin / "lib", private_plugin / "lib")
    (private_plugin / "dotnet").mkdir()
    shutil.copyfile(host_output / (HOST + ".dll"), private_plugin / "dotnet" / (HOST + ".dll"))
    build = "readiness-" + hashlib.sha256((output / (TOOLS + ".dll")).read_bytes()).hexdigest()[:12]
    destination = root / "input" / HOST / "tools" / build
    destination.mkdir(parents=True)
    for suffix in [".dll", ".pdb"]:
        shutil.copyfile(output / (TOOLS + suffix), destination / (TOOLS + suffix))
    (destination / "host.sha256").write_text(identity, encoding="ascii")
    return build


def build_analyzer(root, generator, env):
    project = root / ("with-generator" if generator else "analyzer-only")
    project.mkdir()
    shutil.copyfile(FIXTURES / "FixtureAnalyzer.cs", project / "FixtureAnalyzer.cs")
    if generator:
        shutil.copyfile(FIXTURES / "FixtureGenerator.cs", project / "FixtureGenerator.cs")
    (project / "FixtureChecks.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework>'
        '</PropertyGroup><ItemGroup><Reference Include="$(MSBuildToolsPath)/Roslyn/bincore/Microsoft.CodeAnalysis.dll;'
        '$(MSBuildToolsPath)/Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll" Private="false"/></ItemGroup></Project>')
    dotnet("build", project, "-c", "Release", env=env)
    return project / "bin/Release/net10.0/FixtureChecks.dll"


def prepare_solution(folder, case, analyzers, env):
    if case == "generator":
        project = folder / REAL_PROJECT
        shutil.copytree(REPOSITORY / "tools" / REAL_PROJECT, project, ignore=shutil.ignore_patterns("bin", "obj"))
    else:
        project = folder / "Probe"
        project.mkdir(parents=True)
        # A .NET Framework project avoids the framework-provided analyzers in modern .NET targeting packs.
        (project / "Probe.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
            '<TargetFramework>net472</TargetFramework><EnableNETAnalyzers>false</EnableNETAnalyzers>'
            '</PropertyGroup></Project>')
    if case in analyzers:
        (folder / "Directory.Build.targets").write_text(
            f'<Project><ItemGroup><Analyzer Include="{escape(str(analyzers[case]))}"/></ItemGroup></Project>')
    probe = project / "ReadinessProbe.cs"
    expression = "Shapes.RoundThreeGenerated.Twice(21)" if case == "generator" else "21"
    probe.write_text("public class ReadinessProbe\n{\n    public int RoundThreeAnalyzerTarget() => " + expression + ";\n}\n")
    solution = folder / "Readiness.slnx"
    solution.write_text(f'<Solution><Project Path="{project.name}/{project.name}.csproj"/></Solution>')
    dotnet("restore", solution, env=env)
    return solution, probe


def call(port, name, arguments):
    request = json.dumps({"jsonrpc": "2.0", "id": 1, "method": "tools/call",
                          "params": {"name": name, "arguments": arguments}}).encode()
    with HTTP.open(urllib.request.Request(f"http://127.0.0.1:{port}/", data=request,
                   headers={"Content-Type": "application/json"}), timeout=30) as response:
        result = json.load(response)["result"]
    if result.get("isError"):
        raise AssertionError(result["content"])
    return result["content"][0]["text"]


def check_first_ready(port, case, probe, usages_first):
    def diagnostics():
        result = json.loads(call(port, "get_diagnostics", {"filePath": str(probe), "minSeverity": "warning"}))
        messages = [it["message"] for it in result["diagnostics"]]
        assert ("Round three analyzer reached RoundThreeAnalyzerTarget" in messages) == (case != "no-analyzers"), result
        assert all(it["severity"] != "error" for it in result["diagnostics"]), result
        return result

    def generated_usage():
        line = probe.read_text().splitlines()[2]
        usages = call(port, "find_usages", {"filePath": str(probe), "line": 3, "column": line.index("Twice") + 2})
        assert "declared: [generated]" in usages and "1 usages in 1 files" in usages, usages
        return usages

    if case != "generator":
        return {"diagnostics": diagnostics()}
    if usages_first:
        return {"usages": generated_usage(), "diagnostics": diagnostics()}
    return {"diagnostics": diagnostics(), "usages": generated_usage()}


def cold_open(folder, case, analyzers, env, options, build, usages_first):
    solution, probe = prepare_solution(folder / "checkout", case, analyzers, env)
    common = ["pwsh", "-NoProfile", "-File", str(HARNESS)]
    profile = ["-Profile", str(folder / "profile")]
    start = common + ["start", *profile, "-RiderHome", str(options.rider), "-Solution", str(solution),
                      "-ToolsBuild", build, "-RemoteDevPort", str(options.remote_port), "-RoyderPort", str(options.port)]
    child_env = {**env, "LOCALAPPDATA": str(folder.parent / "input"), "APPDATA": str(folder.parent / "roaming-input")}
    history = []
    deadline = time.monotonic() + 210
    with (folder / "start.log").open("w") as log:
        launcher = subprocess.Popen(start, env=child_env, stdout=log, stderr=subprocess.STDOUT)
        try:
            while time.monotonic() < deadline:
                assert launcher.poll() in (0, None), (folder / "start.log").read_text()
                try:
                    with HTTP.open(f"http://127.0.0.1:{options.port}/health", timeout=2) as response:
                        health = json.load(response)
                except (urllib.error.URLError, TimeoutError):
                    time.sleep(.1)
                    continue
                for item in health["solutions"]:
                    if item["path"] != str(solution) or item.get("tools", {}).get("build") != build:
                        continue
                    assert "sourceGeneratorHealthFailed" not in item, item
                    ready = item["sourceGeneratorsReady"]
                    history.append({"time": time.time(), "indexing": item["indexing"], "sourceGeneratorsReady": ready})
                    if ready is False:
                        continue
                    assert ready is (None if case == "no-analyzers" else True), item
                    if item["indexing"]:
                        continue
                    answers = check_first_ready(options.port, case, probe, usages_first)
                    (folder / "answers.json").write_text(json.dumps(answers, indent=2))
                    return
                time.sleep(.1)
            raise AssertionError(f"Readiness timed out: {history[-1:]}")
        finally:
            (folder / "health.json").write_text(json.dumps(history, indent=2))
            # Startup and shutdown serialize on the harness's commands.lock.
            try:
                launcher.wait(timeout=260)
            except subprocess.TimeoutExpired:
                # Release this start command's exclusive lock so stop can reach the private job supervisor.
                launcher.kill()
                launcher.wait()
                raise
            finally:
                subprocess.run(common + ["stop", *profile], env=child_env, check=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rider", type=Path, default=Path(os.environ["LOCALAPPDATA"]) / "Programs/Rider")
    parser.add_argument("--remote-port", type=int, default=25031)
    parser.add_argument("--port", type=int, default=25041)
    parser.add_argument("--repeat", type=int, default=2)
    options = parser.parse_args()
    if options.repeat < 1:
        parser.error("--repeat must be positive")
    root = Path(tempfile.mkdtemp(prefix="royder-source-generator-readiness-"))
    print("Evidence:", root, flush=True)
    env = {**os.environ, "DOTNET_CLI_HOME": str(root / "cli"), "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH": "false",
           "DOTNET_CLI_TELEMETRY_OPTOUT": "1",
           "NUGET_PACKAGES": os.environ.get("NUGET_PACKAGES", str(Path.home() / ".nuget/packages")),
           "DOTNET_GENERATE_ASPNET_CERTIFICATE": "false", "DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE": "1"}
    build = stage_tools(root, options.rider, env)
    analyzers = {case: build_analyzer(root, case == "generator", env) for case in ("generator", "analyzer-only")}
    for iteration in range(options.repeat):
        for case in ("generator", "no-analyzers", "analyzer-only"):
            folder = root / f"{iteration + 1}-{case}"
            cold_open(folder, case, analyzers, env, options, build, usages_first=iteration % 2 == 0)
            print("Passed:", folder.name, flush=True)
