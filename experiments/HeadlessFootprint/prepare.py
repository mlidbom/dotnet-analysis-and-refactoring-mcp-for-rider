"""Set up one disposable experiment. Run with python -B prepare.py ORIGINAL_CHECKOUT."""
import os
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
HOME = ROOT / "environment"
REPO = ROOT / "fixture-repository"
HOST = ROOT / "HeadlessHost"
SOLUTION = "DotNetAnalysisAndRefactoringMcp.FixtureSolution.slnx"
PROGRAM = "IShape.Area"
JB = Path(os.environ["USERPROFILE"]) / ".dotnet/tools/.store/jetbrains.resharper.globaltools/2026.3.0.1/jetbrains.resharper.globaltools/2026.3.0.1/tools/net10.0/any"
NO_WINDOW = subprocess.CREATE_NO_WINDOW


def private_environment():
    env = os.environ.copy()
    locations = {"DOTNET_CLI_HOME": "cli", "NUGET_PACKAGES": "packages", "NUGET_HTTP_CACHE_PATH": "nuget-http",
                 "NUGET_PLUGINS_CACHE_PATH": "nuget-plugins", "TEMP": "tmp", "TMP": "tmp"}
    for key, directory in locations.items():
        path = HOME / directory
        path.mkdir(parents=True, exist_ok=True)
        env[key] = str(path)
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_GENERATE_ASPNET_CERTIFICATE="false",
               DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE="1", DOTNET_NOLOGO="1",
               GIT_CONFIG_NOSYSTEM="1", GIT_CONFIG_GLOBAL=os.devnull, GIT_TERMINAL_PROMPT="0")
    return env


def run(*args, cwd=ROOT, check=True):
    result = subprocess.run(list(map(str, args)), cwd=cwd, env=private_environment(),
                            creationflags=NO_WINDOW, capture_output=True, text=True, errors="replace")
    if check and result.returncode:
        raise RuntimeError(f"{args} returned {result.returncode}\n{result.stdout}\n{result.stderr}")
    return result


def copy_inputs(source, relative, destination):
    names = run("git", "-C", source, "ls-files", "--", relative).stdout.splitlines()
    if not names:
        raise RuntimeError(f"No tracked inputs under {relative}")
    for name in names:
        target = destination / Path(name).relative_to(relative)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source / name, target)


def dotnet_prep(verb, target):
    return run("dotnet", verb, target, "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false",
               "-p:NuGetAudit=false", "-p:RestoreConfigFile=" + str(HOME / "NuGet.Config"),
               "-v:q", cwd=Path(target).parent if Path(target).is_file() else Path(target))


def restore(target):
    dotnet_prep("restore", target / SOLUTION)


def worktree(label):
    if not label.isalnum():
        raise ValueError("Use one alphanumeric case name")
    path = ROOT / "worktrees" / label
    if path.exists():
        raise ValueError(f"Case already exists: {path}")
    path.parent.mkdir(exist_ok=True)
    run("git", "-C", REPO, "worktree", "add", "--detach", path, "HEAD")
    restore(path)
    if run("git", "-C", path, "status", "--porcelain").stdout:
        raise ValueError("The fixture must be clean before removal.")
    return path


def prepare(source):
    private_environment()
    if HOST.exists() or REPO.exists():
        raise ValueError("Use a fresh scratch directory, or reuse its prepared fixture with the runtime script.")
    (HOME / "NuGet.Config").write_text("<configuration><packageSources><clear/></packageSources></configuration>")
    copy_inputs(source, "experiments/HeadlessHost", HOST)
    copy_inputs(source, "tests/DotNetAnalysisAndRefactoringMcp.FixtureSolution", REPO)
    (REPO / ".gitignore").write_text("bin/\nobj/\n")
    run("git", "init", "-b", "fixture", REPO)
    run("git", "-C", REPO, "add", ".")
    run("git", "-C", REPO, "-c", "user.name=Headless fixture", "-c", "user.email=fixture@localhost",
        "commit", "-m", "Copy the three-project Shapes fixture")
    restore(REPO)
    dotnet_prep("build", HOST / "HeadlessHost.csproj")
    print("Fixture", run("git", "-C", REPO, "rev-parse", "HEAD").stdout.strip())
    print("Source checkout", run("git", "-C", source, "rev-parse", "HEAD").stdout.strip())
    print("Engine", HOST / "bin/Debug/net10.0-windows/HeadlessHost.dll")


if __name__ == "__main__":
    prepare(Path(sys.argv[1]).resolve())
