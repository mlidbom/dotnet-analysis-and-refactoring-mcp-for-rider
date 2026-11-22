"""Evidence for Check-Settings.ps1. All generated settings and source bytes live in the private run directory."""
from hashlib import sha256
import json
import os
from pathlib import Path
import re
import shutil
import sys
import xml.etree.ElementTree as ET

from compare_read_tools import PORTS, REPOSITORY, SHELL, call, save


JETBRAINS = Path(os.environ["APPDATA"]) / "JetBrains"
GLOBAL = JETBRAINS / "Rider2026.3/resharper-host/GlobalSettingsStorage.DotSettings"
PERSONAL = JETBRAINS / "Shared/vAny/MyResharperSettings.DotSettings"
KEY = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"


def hashes(experiment):
    solution = REPOSITORY / "ReSharperMcp.sln"
    projects = [REPOSITORY / name for name in re.findall(r'^Project\(.*?\) = "[^"]+", "([^"]+\.csproj)"',
                                                         solution.read_text(encoding="utf-8-sig"), re.MULTILINE)]
    files = [*JETBRAINS.rglob("*.DotSettings"), *JETBRAINS.rglob("*.DotSettings.user"),
             *(Path(str(project) + suffix) for project in [solution, *projects] for suffix in (".DotSettings", ".DotSettings.user")),
             REPOSITORY / ".editorconfig", *experiment.glob("*.DotSettings"), experiment / "source/.editorconfig"]
    return {str(path): sha256(path.read_bytes()).hexdigest() if path.is_file() else None for path in files}


def global_keys():
    return {entry.get(KEY): sha256(ET.tostring(entry)).hexdigest() for entry in ET.parse(GLOBAL).getroot()}


def prepare(experiment, project_folder):
    save(experiment / "paths.json", {"projectFolder": str(project_folder)})
    root = ET.parse(PERSONAL).getroot()
    if not any(element.text == str(PERSONAL) for element in ET.parse(GLOBAL).getroot()):
        raise RuntimeError(f"Rider no longer references {PERSONAL}")
    if any("/FileInjectedLayer/" in element.get(KEY, "") for element in root):
        raise RuntimeError("The personal layer now refers to other settings files; inspect those before mounting a copy.")
    shutil.copyfile(PERSONAL, experiment / "personal.DotSettings")
    # Keep the original XAML namespace declarations when making the small experimental profiles.
    original = PERSONAL.read_text(encoding="utf-8-sig")
    start = original.index("<wpf:ResourceDictionary")
    header = original[start:original.index(">", start) + 1]
    for name, keys in [("two-keys", ["=MemberCanBeInternal/", "=UnusedMember_002EGlobal/"]),
                       ("three-keys", ["=MemberCanBeInternal/", "=UnusedMember_002EGlobal/", "/IncludeWarningsInSwea/"])]:
        entries = [line.strip() for line in original.splitlines() if any(key in line for key in keys)]
        if len(entries) != len(keys):
            raise RuntimeError(f"Expected {len(keys)} settings in {name}, found {len(entries)}.")
        (experiment / f"{name}.DotSettings").write_text("\n".join([header, *entries, "</wpf:ResourceDictionary>"]), encoding="utf-8")
    [indent] = [line for line in original.splitlines() if "/CSharpFormat/INDENT_SIZE/@" in line]
    if ">3<" not in indent:
        raise RuntimeError("Expected the personal C# indent size to be 3.")
    (experiment / "conflict.DotSettings").write_text(original.replace(indent, indent.replace(">3<", ">7<")), encoding="utf-8")
    source = experiment / "source"
    contents = ("using System;\r\nfile static class SettingsParityFixture\r\n{\r\n"
                "   public static void Register(Action<Action> register, IDisposable disposable)\r\n"
                "      => register(() => disposable.Dispose());\r\n}\r\n").encode()
    for file in ("Control.cs", "Raised.cs"):
        (source / file).write_bytes(contents)
    (source / ".editorconfig").write_text("[Control.cs]\nresharper_convert_closure_to_method_group_highlighting = suggestion\n"
                                         "[Raised.cs]\nresharper_convert_closure_to_method_group_highlighting = error\n", encoding="utf-8")
    save(experiment / "hashes-before.json", hashes(experiment))
    save(experiment / "global-keys-before.json", global_keys())


def inject(experiment, scratch):
    """Give a private global layer the same file injection as Rider, targeting only the scratch profile."""
    directory = scratch / "roaming/Shared/vAny"
    directory.mkdir(parents=True, exist_ok=True)
    original = ET.parse(GLOBAL).getroot()
    [pointer] = [entry for entry in original if entry.text == str(PERSONAL) and "/FileInjectedLayer/" in entry.get(KEY, "")]
    identity = pointer.get(KEY).split("/=")[1].split("/")[0]
    private = ET.Element(original.tag)
    profile = experiment / "conflict.DotSettings"
    for entry in original:
        key = entry.get(KEY, "")
        if f"/FileInjectedLayer/={identity}/" in key or f"/InjectedLayerCustomization/=File{identity}/" in key:
            if "/AbsolutePath/" in key:
                entry.text = str(profile)
            if "/RelativePath/" in key:
                # RegisterInjectedMounts uses the hosting file's DiskFilePath itself as the base.
                entry.text = os.path.relpath(profile, directory / "GlobalSettingsStorage.DotSettings")
            private.append(entry)
    ET.register_namespace("wpf", "http://schemas.microsoft.com/winfx/2006/xaml/presentation")
    ET.register_namespace("x", "http://schemas.microsoft.com/winfx/2006/xaml")
    ET.register_namespace("s", "clr-namespace:System;assembly=mscorlib")
    ET.ElementTree(private).write(directory / "GlobalSettingsStorage.DotSettings", encoding="utf-8")


def capture(experiment, case, scratch):
    out = experiment / case
    out.mkdir()
    project_folder = Path(json.loads((experiment / "paths.json").read_text())["projectFolder"])
    save(out / "engine.json", {"directory": str(scratch)})
    for name in ("effective.txt", "mounts.tsv", "components.txt"):
        shutil.copyfile(scratch / name, out / name)
    if (scratch / "cached-issues.txt").is_file():
        shutil.copyfile(scratch / "cached-issues.txt", out / "cached-issues.txt")
    if case == "injected-conflict":
        paths = [row.split("\t")[-1] for row in (out / "mounts.tsv").read_text(encoding="utf-8-sig").splitlines()]
        if str(experiment / "conflict.DotSettings") not in paths:
            raise RuntimeError("The headless container did not mount the intended injected file.")
    source = SHELL.read_bytes()
    first = source.index(b"void WatchdogPing()")
    last = source.index(b"\n   }", first) + len(b"\n   }")
    declaration = source[first:last].decode()
    answers = {}
    for label, tool, args in [
        ("file", "inspect_code", dict(filePath=str(SHELL), minSeverity="suggestion", maxResults=50)),
        ("control", "get_diagnostics", dict(filePath=str(project_folder / "Control.cs"), minSeverity="hint")),
        ("raised", "get_diagnostics", dict(filePath=str(project_folder / "Raised.cs"), minSeverity="hint")),
        ("source", "get_symbol_source", dict(symbolName="DotNetAnalysisAndRefactoringMcp.McpShellComponent.WatchdogPing", kind="method")),
    ]:
        if label in {"control", "raised"} and not project_folder.exists():
            continue
        body = dict(jsonrpc="2.0", id=1, method="tools/call", params=dict(name=tool, arguments=dict(solutionName=REPOSITORY.name, **args)))
        save(out / f"{label}-request.json", body)
        for host, port in PORTS.items():
            reply = call(port, body)
            save(out / f"{label}-{host}.json", reply)
            if "result" not in reply or reply["result"].get("isError"):
                raise RuntimeError(f"{host}: {reply}")
            text = reply["result"]["content"][0]["text"]
            if label == "file":
                answers[f"{label}-{host}"] = text.splitlines()
            elif label == "source":
                returned = json.loads(text)["declarations"][0]["source"]
                answers[f"{label}-{host}"] = {"CRLF": returned.count("\r\n"), "LF": returned.count("\n"),
                                              "equalsDisk": returned == declaration, "equalsDiskWithLF": returned == declaration.replace("\r\n", "\n")}
            else:
                result, _ = json.JSONDecoder().raw_decode(text)
                if "error" in result:
                    raise RuntimeError(f"{host}: {result}")
                matches = [it["severity"] for it in result["diagnostics"] if it["inspectionId"] == "ConvertClosureToMethodGroup"]
                answers[f"{label}-{host}"] = matches
    answers["disk"] = {"CRLF": declaration.count("\r\n"), "LF": declaration.count("\n")}
    save(out / "summary.json", answers)
    save(out / "hashes.json", hashes(experiment))
    print(case, answers["file-headless"][0], flush=True)
    if "control-headless" in answers:
        print({key: val for key, val in answers.items() if key.startswith(("control", "raised"))}, flush=True)


def verify(experiment):
    before = json.loads((experiment / "hashes-before.json").read_text())
    after = hashes(experiment)
    save(experiment / "hashes-after.json", after)
    old_keys = json.loads((experiment / "global-keys-before.json").read_text())
    new_keys = global_keys()
    save(experiment / "global-changed-keys.json", sorted(key for key in old_keys.keys() | new_keys.keys()
                                                        if old_keys.get(key) != new_keys.get(key)))
    changed = [path for path in before.keys() | after.keys() if before.get(path) != after.get(path)]
    if changed:
        raise RuntimeError(f"Changed settings files: {changed}. See global-changed-keys.json for Rider's global keys.")
    print(f"Verified {len(after)} settings paths, including absent solution layers.")


if __name__ == "__main__":
    mode, directory, *args = sys.argv[1:]
    if mode == "prepare":
        prepare(Path(directory), Path(args[0]))
    elif mode == "inject":
        inject(Path(directory), Path(args[0]))
    elif mode == "capture":
        capture(Path(directory), args[0], Path(args[1]))
    elif mode == "verify":
        verify(Path(directory))
