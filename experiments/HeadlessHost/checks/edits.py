from .engine import SOLUTION
from .probe_files import AFTER_METHOD, BEFORE_METHOD, CALLER, MOVED, NAMESPACE, SOURCE, TYPE
from .rider_document import RiderDocuments


def disk(path):
    return path.read_text(encoding="utf-8-sig") if path.exists() else None


def check_edit(engine, case):
    engine.require_solution(23751, SOLUTION)
    with RiderDocuments() as rider:
        return compare_edit(engine, case, rider)


def compare_edit(engine, case, rider):
    engine.console("refactorings")
    paths = [SOURCE, CALLER] if case in {"rename", "move-type"} else [SOURCE]
    for path in paths:
        assert engine.document(path)["text"] is not None, path
        if not rider.compare(path, disk(path), engine):
            raise RuntimeError(f"Rider differs from disk before the edit: {path}")
    if case == "fix-usings":
        engine.call("get_file_errors", filePath=str(SOURCE))
        engine.console("trace " + str(SOURCE))

    if case == "rename":
        engine.call("rename_symbol", symbolName=TYPE + "." + BEFORE_METHOD, newName=AFTER_METHOD)
    elif case == "fix-usings":
        engine.call("fix_usings", filePath=str(SOURCE))
    else:
        engine.call("move_type_to_namespace", symbolName=TYPE, newNamespace=NAMESPACE + ".Destination")

    # Observe the engine before another Royder call can reconcile its documents with disk.
    if case == "move-type":
        paths.append(MOVED)
    for path in paths:
        document = engine.document(path)
        on_disk = engine.observe("disk", disk(path), path=str(path))
        text = document["text"]
        engine.observe("headless document matches disk", on_disk == (text.replace("\r\n", "\n") if text is not None else None),
                       path=str(path), projectItems=document["projectItems"])
    for path in paths:
        if path.exists():
            rider.compare(path, disk(path), engine)

    # A fresh HTTP request exercises normal pre-call synchronization after the edit.
    if case == "fix-usings":
        engine.call("get_file_errors", filePath=str(SOURCE))
    elif case == "rename":
        for member in (BEFORE_METHOD, AFTER_METHOD):
            engine.call("search_symbol", query=f"{TYPE}.{member}", kinds="method")
    else:
        for namespace in (NAMESPACE, NAMESPACE + ".Destination"):
            engine.call("search_symbol", query=f"{namespace}.{TYPE}", kinds="type")

    if case == "rename":
        return all(AFTER_METHOD in (disk(path) or "") and BEFORE_METHOD not in disk(path) for path in paths)
    if case == "fix-usings":
        return f"using {NAMESPACE}.Dependency;" in (disk(SOURCE) or "")
    caller = disk(CALLER) or ""
    imported = f"using {NAMESPACE}.Destination;" in caller or f"Destination.{TYPE}" in caller
    return not SOURCE.exists() and f"namespace {NAMESPACE}.Destination" in (disk(MOVED) or "") and imported
