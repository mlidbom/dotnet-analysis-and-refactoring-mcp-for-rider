"""Called by Run.ps1 -CompareReadTools with the private directory for this run."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import time
from urllib.request import ProxyHandler, Request, build_opener


REPOSITORY = Path(__file__).resolve().parents[2]
SHELL = REPOSITORY / "src/DotNetAnalysisAndRefactoringMcp/McpShellComponent.cs"
SERVING = "DotNetAnalysisAndRefactoringMcp.Serving"
PORTS = {"rider": 23751, "headless": 23900}
LOCAL_HTTP = build_opener(ProxyHandler({}))


def position(line_fragment, caret_after):
    """Resolve a unique source line and a one-based caret without editing the file."""
    [(line_number, line)] = [(i, line) for i, line in enumerate(SHELL.read_text(encoding="utf-8-sig").splitlines(), 1)
                             if line_fragment in line]
    return dict(filePath=str(SHELL), line=line_number, column=line.index(caret_after) + len(caret_after) + 1)


def cases():
    method = dict(symbolName="DotNetAnalysisAndRefactoringMcp.McpShellComponent.WatchdogPing", kind="method")
    base = dict(symbolName=f"{SERVING}.OpenSolution", kind="type")
    derived = dict(symbolName=f"{SERVING}.LocalSolution", kind="type")
    file = dict(filePath=str(SHELL))
    yield "inventory", "list_solutions", {}
    yield "usages", "find_usages", method
    yield "members", "get_symbol_info", dict(**base, includeMembers=True)
    yield "implementations", "find_implementations", base
    yield "search-source", "search_symbol", dict(query="McpShellComponent", kinds="type")
    compiled = "Newtonsoft.Json.JsonReaderException"
    yield "search-libraries", "search_symbol", dict(query=compiled, kinds="type", includeLibraries=True)
    for output in ("text", "json"):
        yield f"definition-{output}", "go_to_definition", dict(**method, format=output)
        yield f"namespace-{output}", "browse_namespace", dict(namespaceName=SERVING, format=output)
    yield "projects", "get_solution_structure", {}
    yield "declarations", "list_symbols_in_file", file
    yield "file-errors", "get_file_errors", file
    yield "changed-errors", "get_file_errors", dict(changedSince="2656956")
    yield "flow", "flow", dict(**method, depth=1)
    yield "source", "get_symbol_source", method
    yield "compiled-source", "get_symbol_source", dict(symbolName=compiled, kind="type")
    for direction in ("incoming", "outgoing"):
        yield direction, "get_call_hierarchy", dict(**method, direction=direction, maxDepth=1)
    yield "bases", "get_type_hierarchy", dict(**derived, direction="supertypes", maxDepth=1)
    yield "subtypes", "get_type_hierarchy", dict(**base, direction="subtypes", maxDepth=1)
    yield "diagnostics", "get_diagnostics", dict(**file, minSeverity="hint")
    no_braces = position("if(_server == null) return;", "if")
    yield "editorconfig", "get_diagnostics", dict(**file, minSeverity="hint",
                                                 position=dict(line=no_braces["line"], column=no_braces["column"]))
    yield "file-inspections", "inspect_code", dict(**file, minSeverity="suggestion", maxResults=50)
    yield "solution-inspections", "inspect_code", dict(minSeverity="hint", inspectionIds=["UnusedMember.Global"], maxResults=30)
    yield "bulb", "list_quick_fixes", position("() => watchdog.Dispose()", "watchdog.Dis")
    yield "complete-environment", "complete_at", dict(**position("var envPort = Environment.", "Environment.GetEnvironmentVariab"), maxResults=20)
    yield "complete-logger", "complete_at", dict(**position("MCP server health check failed", "_logger."), maxResults=180)
    yield "rename-preview", "rename_symbol", dict(**method, newName="ProbeWatchdogPing", dryRun=True)


def worktree_digest():
    """Hash names and bytes of tracked and nonignored files, including symlink targets as text."""
    paths = subprocess.check_output(["git", "ls-files", "-coz", "--exclude-standard"], cwd=REPOSITORY)
    digest = hashlib.sha256()
    for name in sorted(set(paths.rstrip(b"\0").split(b"\0"))):
        path = REPOSITORY / os.fsdecode(name)
        if path.is_symlink():
            contents = os.fsencode(os.readlink(path))
        elif path.is_file():
            contents = path.read_bytes()
        elif not path.exists():
            contents = b"<missing>"
        else:
            raise ValueError(f"Unexpected git path {path}")
        digest.update(name + b"\0" + hashlib.sha256(contents).digest())
    return digest.hexdigest()


def save(path, data):
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def call(port, body):
    request = Request(f"http://127.0.0.1:{port}/", data=json.dumps(body).encode(),
                      headers={"Content-Type": "application/json"})
    try:
        with LOCAL_HTTP.open(request, timeout=145) as reply:
            return json.load(reply)
    except (OSError, ValueError) as error:
        return {"transportError": str(error)}


def answer(reply, tool):
    if "transportError" in reply or "error" in reply:
        return "failed", reply
    result = reply["result"]
    if result.get("isError"):
        return "failed", result
    if tool == "tools/list":
        return "ok", result
    [content] = result["content"]
    text = content["text"]
    try:
        value = json.loads(text)
    except ValueError:
        if tool == "inspect_code":
            text = re.sub(r"(?<= in )\d+\.\d+ s(?=\.)", "<elapsed>", text, count=1)
        return "ok", text
    if isinstance(value, dict) and "error" in value:
        return "failed", value
    if tool == "get_symbol_source" and not any(it.get("source") for it in value["declarations"]):
        return "failed", value
    return "ok", value


def main(directory):
    evidence = directory / "comparison"
    evidence.mkdir()
    before = worktree_digest()
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=REPOSITORY, text=True).strip()
    save(evidence / "revision.json", dict(commit=commit, worktreeSHA256=before))
    rows = []
    for case, tool, arguments in [("tools", "tools/list", {}), *cases()]:
        if tool == "tools/list":
            body = dict(jsonrpc="2.0", id=1, method="tools/list", params={})
        else:
            body = dict(jsonrpc="2.0", id=1, method="tools/call",
                        params=dict(name=tool, arguments=dict(solutionName=REPOSITORY.name, **arguments)))
        case_dir = evidence / case
        case_dir.mkdir()
        save(case_dir / "request.json", body)
        answers = {}
        for host, port in PORTS.items():
            started = time.monotonic()
            reply = call(port, body)
            save(case_dir / f"{host}.json", reply)
            answers[host] = answer(reply, tool)
            print(f"{case} {host} {answers[host][0]} ({time.monotonic() - started:.1f}s)", flush=True)
            if tool == "list_solutions":
                expected = str(REPOSITORY / "ReSharperMcp.sln")
                solutions = answers[host][1]["solutions"]
                if not any(it["path"] == expected for it in solutions):
                    raise RuntimeError(f"{host} does not have {expected} open")
            if tool == "rename_symbol":
                after = worktree_digest()
                save(case_dir / f"{host}-disk.json", dict(before=before, after=after))
                if after != before:
                    raise RuntimeError(f"The worktree changed by the end of {host}'s dry run")
                state, preview = answers[host]
                if state == "ok" and (preview.get("dryRun") is not True or preview.get("applied") is not False):
                    raise RuntimeError(f"{host} did not confirm a dry run")
        if any(state == "failed" for state, _ in answers.values()):
            status = "failed"
        else:
            status = "same" if answers["rider"] == answers["headless"] else "different"
        rows.append(dict(case=case, tool=tool, status=status))
        save(evidence / "results.json", rows)
    if worktree_digest() != before:
        raise RuntimeError("The worktree changed during comparison")


if __name__ == "__main__":
    main(Path(sys.argv[1]))
