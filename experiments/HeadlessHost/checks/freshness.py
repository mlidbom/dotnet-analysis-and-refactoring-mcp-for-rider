import re
import time

from .engine import HEADLESS_PORT, RIDER_PORT, SOLUTION
from .rider_backend import require_document


def locations(engine, name, port=HEADLESS_PORT):
    # No file argument: the results must come from Royder's solution-wide symbol index.
    answer = engine.call("search_symbol", port=port, query=name, kinds="type")
    count = re.search(r"\b(\d+) results?", answer)
    if not count:
        return None
    rows = [line for line in answer.splitlines() if " — " in line and not line.startswith("query:")]
    return rows if len(rows) == int(count[1]) else None


def finds_change(engine, case, files, port=HEADLESS_PORT):
    name = files.changed_subject if case == "content" else files.subject
    found = locations(engine, name, port)
    if case == "delete":
        return found == []
    expected = files.moved if case == "move-file" else files.source
    if found is None or len(found) != 1 or str(expected).lower() not in found[0].lower():
        return False
    return case != "content" or locations(engine, files.subject, port) == []


def check_freshness(engine, case, files):
    before = locations(engine, files.subject)
    assert before is not None and len(before) == (0 if case == "add" else 1), before
    if case != "add":
        assert files.subject in engine.document(files.source)["text"]
    if files.paired:
        engine.require_solution(RIDER_PORT, SOLUTION)
        if case != "add":
            require_document(engine, files.source)
    if case == "content":
        engine.console("trace " + str(files.source))

    started = time.monotonic()
    if case == "content":
        files.write_type(files.source, files.changed_subject)
    elif case == "add":
        files.write_type(files.source)
    elif case == "delete":
        files.source.unlink()
    elif case == "move-file":
        files.source.rename(files.moved)

    def read(phase):
        correct = finds_change(engine, case, files)
        engine.observe("freshness", correct, phase=phase, sinceChange=round(time.monotonic() - started, 3))
        return correct

    if case == "content":
        engine.document(files.source)
    for phase, delay in (("first call", 0), ("second call", 0), ("after 2 seconds", 2), ("after 12 seconds", 10)):
        time.sleep(delay)
        if read(phase):
            return phase
    engine.console("refresh")
    if read("after CommitChanges"):
        return "after CommitChanges"
    time.sleep(1)
    if read("one second after CommitChanges"):
        return "one second after CommitChanges"
    engine.call("reload_projects", filePath=str(files.project))
    if read("after reload_projects"):
        return "after reload_projects"
    return "not seen in this session"
