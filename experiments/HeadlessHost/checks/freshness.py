import re
import time

from .probe_files import AFTER, MOVED, PROJECT_FILE, SOURCE, TYPE, write_type


def locations(engine, name):
    # No file argument: the results must come from Royder's solution-wide symbol index.
    answer = engine.call("search_symbol", query=name, kinds="type")
    count = re.search(r"\b(\d+) results?", answer)
    if not count:
        return None
    rows = [line for line in answer.splitlines() if " — " in line and not line.startswith("query:")]
    return rows if len(rows) == int(count[1]) else None


def check_freshness(engine, case):
    before = locations(engine, TYPE)
    assert before is not None and len(before) == (0 if case == "add" else 1), before
    if case != "add":
        assert TYPE in engine.document(SOURCE)["text"]
    if case == "content":
        engine.console("trace " + str(SOURCE))

    name = AFTER if case == "content" else TYPE
    expected = MOVED if case == "move-file" else SOURCE
    started = time.monotonic()
    if case == "content":
        write_type(SOURCE, AFTER)
    elif case == "add":
        write_type(SOURCE)
    elif case == "delete":
        SOURCE.unlink()
    elif case == "move-file":
        SOURCE.rename(MOVED)

    def read(phase):
        found = locations(engine, name)
        correct = found == [] if case == "delete" else (
            found is not None and len(found) == 1 and str(expected).lower() in found[0].lower())
        if correct and case == "content":
            correct = locations(engine, TYPE) == []
        engine.observe("freshness", correct, phase=phase, sinceChange=round(time.monotonic() - started, 3))
        return correct

    if case == "content":
        engine.document(SOURCE)
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
    engine.call("reload_projects", filePath=str(PROJECT_FILE))
    if read("after reload_projects"):
        return "after reload_projects"
    return "not seen in this session"
