import re

from .engine import RIDER_PORT, SOLUTION
from .probe_files import AFTER_METHOD, BEFORE_METHOD
from .rider_document import RiderDocuments
from .rider_backend import check_document, require_document


def disk(path):
    return path.read_text(encoding="utf-8-sig") if path.exists() else None


def check_edit(engine, case, files):
    engine.require_solution(RIDER_PORT, SOLUTION)
    with RiderDocuments() as rider:
        return compare_edit(engine, case, files, rider)


def compare_edit(engine, case, files, rider):
    engine.console("refactorings")
    paths = [files.source, files.caller] if case in {"rename", "move-type"} else [files.source]
    for path in paths:
        assert engine.document(path)["text"] is not None, path
        if files.paired:
            require_document(engine, path)
        if not rider.compare(path, disk(path), engine):
            raise RuntimeError(f"Rider differs from disk before the edit: {path}")
    if case == "fix-usings":
        before = engine.call("get_file_errors", filePath=str(files.source))
        if f"Cannot resolve symbol '{files.import_name}'" not in before:
            raise RuntimeError("The headless project did not see the intended missing import.")
        if files.paired:
            ordinary = engine.call("get_file_errors", port=RIDER_PORT, filePath=str(files.source))
            if not re.search(r"\b0 diagnostics\s*$", ordinary):
                raise RuntimeError("Rider reported diagnostics in the ordinary-build source.")
        engine.console("trace " + str(files.source))

    if case == "rename":
        engine.call("rename_symbol", symbolName=files.subject + "." + BEFORE_METHOD, newName=AFTER_METHOD)
    elif case == "fix-usings":
        engine.call("fix_usings", filePath=str(files.source))
    else:
        engine.call("move_type_to_namespace", symbolName=files.subject, newNamespace=files.namespace + ".Destination")

    # Observe the engine before another Royder call can reconcile its documents with disk.
    if case == "move-type":
        paths.append(files.moved)
    for path in paths:
        document = engine.document(path)
        on_disk = engine.observe("disk", disk(path), path=str(path))
        text = document["text"]
        engine.observe("headless document matches disk", on_disk == (text.replace("\r\n", "\n") if text is not None else None),
                       path=str(path), projectItems=document["projectItems"])
    for path in paths:
        if path.exists():
            rider.compare(path, disk(path), engine)
            if files.paired:
                check_document(engine, path)

    # A fresh HTTP request exercises normal pre-call synchronization after the edit.
    if case == "fix-usings":
        engine.call("get_file_errors", filePath=str(files.source))
        if files.paired:
            engine.call("get_file_errors", port=RIDER_PORT, filePath=str(files.source))
    elif case == "rename":
        for member in (BEFORE_METHOD, AFTER_METHOD):
            engine.call("search_symbol", query=f"{files.subject}.{member}", kinds="method")
    else:
        for namespace in (files.namespace, files.namespace + ".Destination"):
            engine.call("search_symbol", query=f"{namespace}.{files.subject}", kinds="type")

    if case == "rename":
        return all(AFTER_METHOD in (disk(path) or "") and BEFORE_METHOD not in disk(path) for path in paths)
    if case == "fix-usings":
        return f"using {files.namespace}.Dependency;" in (disk(files.source) or "")
    caller = disk(files.caller) or ""
    imported = f"using {files.namespace}.Destination;" in caller or f"Destination.{files.subject}" in caller
    return not files.source.exists() and f"namespace {files.namespace}.Destination" in (disk(files.moved) or "") and imported
