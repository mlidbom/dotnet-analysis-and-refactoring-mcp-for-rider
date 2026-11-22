"""Compare the two daemons on identical project files before and after an edit."""

import json
import re
from collections import Counter

from .editing_services import snapshot
from .engine import LiveEngine
from .probe_files import FIXTURE_PROJECT, ProbeFiles


def inspection_answers(engine, files):
    arguments = dict(filePath=str(files.source), minSeverity="hint")
    diagnostics = json.loads(engine.call("get_diagnostics", **arguments))
    if "diagnostics" not in diagnostics:
        raise RuntimeError(f"Missing diagnostic output: {diagnostics}")
    answers = {"get_diagnostics": diagnostics}
    for name, scope in (("inspect_file", {"filePath": str(files.source)}), ("inspect_solution", {})):
        raw = engine.call("inspect_code", **scope, minSeverity="hint", maxResults=500)
        if not re.match(r"\d+ issues at ", raw) or "stopped after " in raw:
            raise RuntimeError(f"Incomplete {name}: {raw}")
        answers[name] = re.sub(r"(?<= in )\d+\.\d+ s(?=\.)", "<elapsed>", raw, count=1)
    return answers


def wait_for_analysis(engine):
    return json.loads(next(s[5:] for s in engine.console("wait-swea") if s.startswith("SWEA ")))


def findings(answer):
    """Preserve each issue's file, location, severity, inspection ID, message and multiplicity."""
    file = None
    rows = Counter()
    for line in answer.splitlines()[2:]:
        if line and not line.startswith(" ") and line != "by inspection:":
            file = line
        elif re.match(r"^  \d+:\d+ ", line):
            if file is None:
                raise ValueError(f"Inspection finding without a file: {line}")
            rows[(file, line.strip())] += 1
    if rows.total() != int(answer.split(" ", 1)[0]):
        raise ValueError("The parsed findings do not account for the inspection count.")
    return rows


def compare_daemons(evidence, request_zones, no_swea):
    files = ProbeFiles("daemon", evidence, FIXTURE_PROJECT)
    runs = {}
    for name, live in (("inspectcode", False), ("live", True)):
        directory = evidence / name
        directory.mkdir()
        with files.installed(), LiveEngine(directory, FIXTURE_PROJECT.with_suffix(".slnx"), probe_missing_import=True,
                                            request_zones=request_zones, live_daemon=live, no_swea=no_swea) as engine:
            services = engine.services()
            ready_before = wait_for_analysis(engine)
            if no_swea and ready_before["enabled"]:
                raise RuntimeError("Solution-wide analysis is enabled in the local-only comparison.")
            before = inspection_answers(engine, files)
            if not any(x["inspectionId"] == "ConvertClosureToMethodGroup" for x in before["get_diagnostics"]["diagnostics"]):
                raise RuntimeError("The fixture did not exercise the intended inspection.")
            engine.call("fix_usings", filePath=str(files.source))
            state = snapshot(engine, files)[files.source.name]
            if not state["documentMatchesDisk"] or f"using {files.namespace}.Dependency;" not in state["text"]:
                raise RuntimeError("The edit did not reach disk and the engine.")
            ready_after = wait_for_analysis(engine)
            if no_swea and ready_after["enabled"]:
                raise RuntimeError("Solution-wide analysis was reenabled after the edit.")
            after = inspection_answers(engine, files)
            runs[name] = {"services": services, "readyBefore": ready_before, "readyAfter": ready_after,
                          "before": before, "after": after, "editedSha256": state["diskSha256"]}
        runs[name]["loggedUnexpectedTransaction"] = "Unexpected transaction in command line" in (directory / "engine.log").read_text(encoding="utf-8")
    result = {phase: {key: runs["inspectcode"][phase][key] == runs["live"][phase][key]
                      for key in runs["inspectcode"][phase]} for phase in ("before", "after")}
    for phase in ("before", "after"):
        readiness = "readyBefore" if phase == "before" else "readyAfter"
        result[phase]["sameAnalysisReadiness"] = all(
            runs["inspectcode"][readiness][key] == runs["live"][readiness][key] for key in ("enabled", "completed"))
        for scope in ("inspect_file", "inspect_solution"):
            manual = findings(runs["inspectcode"][phase][scope])
            live = findings(runs["live"][phase][scope])
            result[phase][scope + "_findings"] = {
                "same": manual == live, "inspectCodeCount": manual.total(), "liveCount": live.total(),
                "onlyInInspectCode": sorted((manual - live).elements()), "onlyInLive": sorted((live - manual).elements()),
            }
    result["sameEditedBytes"] = runs["inspectcode"]["editedSha256"] == runs["live"]["editedSha256"]
    result["loggedUnexpectedTransaction"] = {name: run["loggedUnexpectedTransaction"] for name, run in runs.items()}
    (evidence / "comparison.json").write_text(json.dumps({"result": result, "runs": runs}, indent=2), encoding="utf-8")
    print("Daemon comparison:", result, flush=True)
