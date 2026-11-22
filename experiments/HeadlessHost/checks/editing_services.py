"""Exercise each editing service on its own Git-restored fixture."""

import hashlib
import json
import re

from .probe_files import AFTER_METHOD, BEFORE_METHOD


CASES = ("rename-preview", "rename", "move-type", "quick-fix", "scoped-fix", "generate", "generate-equality",
         "format", "cleanup", "style", "complete", "compiled-source")


def position(path, marker, caret_after=False):
    lines = path.read_text(encoding="utf-8").splitlines()
    (line, content), = [(i, text) for i, text in enumerate(lines, 1) if marker in text]
    column = content.index(marker) + 1 + (len(marker) if caret_after else 0)
    return {"filePath": str(path), "line": line, "column": column}


def snapshot(engine, files, previous_paths=()):
    paths = set(files.folder.rglob("*.cs")) | {files.source} | {files.folder / path for path in previous_paths}
    if files.check == "move-type":
        paths.add(files.moved)
    snapshots = {}
    for path in sorted(paths):
        data = path.read_bytes() if path.is_file() else None
        document = engine.document(path)
        text = None if document["text"] is None else document["text"].replace("\r\n", "\n")
        disk = None if data is None else data.decode("utf-8-sig").replace("\r\n", "\n")
        snapshots[str(path.relative_to(files.folder))] = {
            "diskSha256": hashlib.sha256(data).hexdigest() if data is not None else None,
            "text": disk, "documentText": text, "documentMatchesDisk": disk == text,
            "projectItems": document["projectItems"], "unsaved": document["unsaved"],
        }
    return engine.observe("fixture snapshot", snapshots)


def run_tool(engine, files):
    case = files.check
    source = str(files.source)
    if case in {"rename", "rename-preview"}:
        return engine.call("rename_symbol", symbolName=files.subject + "." + BEFORE_METHOD,
                           newName=AFTER_METHOD, dryRun=case == "rename-preview")
    if case == "move-type":
        return engine.call("move_type_to_namespace", symbolName=files.subject, newNamespace=files.namespace + ".Destination")
    if case in {"quick-fix", "scoped-fix"}:
        diagnostics = json.loads(engine.call("get_diagnostics", filePath=source, minSeverity="hint"))["diagnostics"]
        if not any(d["inspectionId"] == "ConvertClosureToMethodGroup" for d in diagnostics):
            raise RuntimeError("The method-group inspection was missing from the prepared fixture.")
        if case == "quick-fix":
            caret = position(files.source, "Execute();")
            engine.call("list_quick_fixes", **caret)
            return engine.call("apply_quick_fix", **caret, fixId="ToMethodGroupFix")
        return engine.call("apply_suggestions", filePath=source, inspectionIds="ConvertClosureToMethodGroup", scope="file")
    if case in {"generate", "generate-equality"}:
        kind = "constructor" if case == "generate" else "equality-members"
        return engine.call("generate_members", symbolName=files.subject, kind=kind, memberNames=["Quantity"])
    if case in {"format", "cleanup", "style"}:
        return engine.call("format_file", filePath=source, mode=case)
    if case == "complete":
        text = engine.document(files.source)["text"]
        engine.console(f"completion-context {text.index('this.') + len('this.')} {source}")
        return engine.call("complete_at", **position(files.source, "this.", caret_after=True), maxResults=100)
    if case == "compiled-source":
        return engine.call("get_symbol_source", symbolName="System.InvalidOperationException", kind="type")
    raise ValueError(case)


def check_service(engine, files):
    engine.services()
    engine.console("refactorings")
    before = snapshot(engine, files)
    reply = run_tool(engine, files)
    after = snapshot(engine, files, before)
    text = after[files.source.name]["text"] or ""
    changed = before != after
    case = files.check
    if case == "rename-preview":
        preview = json.loads(reply) if reply.lstrip().startswith("{") else {}
        affected = {item["file"] for item in preview.get("changedFiles", [])}
        result = (not changed and preview.get("dryRun") is True and preview.get("applied") is False
                  and preview.get("oldName") == BEFORE_METHOD and preview.get("newName") == AFTER_METHOD
                  and affected == {str(files.source), str(files.caller)})
    elif case == "rename":
        result = all(AFTER_METHOD in (after[path.name]["text"] or "") and BEFORE_METHOD not in after[path.name]["text"]
                     for path in (files.source, files.caller))
    elif case == "move-type":
        destination = after[str(files.moved.relative_to(files.folder))]
        result = (after[files.source.name]["text"] is None and after[files.source.name]["projectItems"] == 0
                  and destination["projectItems"] > 0 and files.namespace + ".Destination" in (destination["text"] or "")
                  and "Destination" in (after[files.caller.name]["text"] or ""))
    elif case in {"quick-fix", "scoped-fix"}:
        result = bool(re.search(r"Handler\(\)\s*=>\s*(?:this\.)?Execute\s*;", text))
    elif case == "generate":
        result = bool(re.search(rf"{files.subject}\s*\(\s*int\s+quantity\s*\)", text)
                      and re.search(r"Quantity\s*=\s*quantity\s*;", text))
    elif case == "generate-equality":
        result = all(re.search(pattern, text) for pattern in (r"Equals\(object ", rf"Equals\({files.subject} ", r"GetHashCode\(\)"))
    elif case == "format":
        result = changed and "left + right" in text
    elif case in {"cleanup", "style"}:
        result = changed and "Identity(int value)" in text and "\n      return result;" in text
        engine.observe("profile edits", {"reformatted": result, "removedPrivate": "private int Identity" not in text,
                                         "convertedToVar": "var result" in text})
    elif case == "complete":
        result = any(item.get("text") in {"KnownProbeMember", "KnownProbeMember()"} for item in json.loads(reply).get("items", []))
    else:
        result = bool(re.search(r"\bclass InvalidOperationException\b", reply))
    disk_changed = any(before.get(path, {}).get("diskSha256") != state["diskSha256"] for path, state in after.items())
    engine.observe("service outcome", {"case": case, "expectedEffect": result, "stateChanged": changed, "diskChanged": disk_changed,
                   "documentsMatchDisk": all(v["documentMatchesDisk"] for v in after.values())})
