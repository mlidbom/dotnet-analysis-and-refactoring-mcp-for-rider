"""Probes: questions taken from the mined episodes, put to a fresh agent that chooses its own tools. Recorded for each
run: the tools it chose, what it cost, and whether its answer holds the facts the probe expects. Run them after a change
to Royder or to the instructions, and now and then as a guard against regressions. dev_docs/what-agents-lack.md says
what they are for.

    python tools/agent_sessions/probe.py run [--probe <id>] --label "<what is live, such as a commit>"
    python tools/agent_sessions/probe.py report

Each run is a line of dev_docs/probe-runs.jsonl. A probe's session is named `royder-probe <id>`, which the miner leaves
out. The agent is Claude Code, with its default model and Magnus's settings; the probe allows it to read, search, run
shell commands, start subagents and call Royder, and tells it to change no files and to put nothing on the unanswered
list: the first run's agent put a suggestion for Magnus there.
"""
import argparse
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import uuid
from collections import Counter
from datetime import datetime, timezone

from episodes import SessionWork
from mine import LOOKED_UP_AS_TEXT, ROYDER_NAVIGATION
from records import CLAUDE_ROOTS, PROBE_SESSION_PREFIX, ClaudeTranscript

HERE = os.path.dirname(os.path.abspath(__file__))
PROBES = os.path.join(HERE, "probes.json")
RUNS = os.path.normpath(os.path.join(HERE, "..", "..", "dev_docs", "probe-runs.jsonl"))
ALLOWED_TOOLS = "Read,Grep,Glob,Bash,PowerShell,ToolSearch,Agent,mcp__dotnet-analysis-and-refactoring"
READ_ONLY = "Answer here only: change no files, and put nothing on the unanswered list."
TIMEOUT_SECONDS = 20 * 60


def run(probe, label):
    session_id = str(uuid.uuid4())
    started = datetime.now(timezone.utc)
    command = [shutil.which("claude"), "-p", "--session-id", session_id, "--name", f"{PROBE_SESSION_PREFIX} {probe['id']}",
               "--output-format", "json", "--allowedTools", ALLOWED_TOOLS]
    finished = subprocess.run(command, input=f"{probe['question']} {READ_ONLY}", cwd=probe["folder"], capture_output=True,
                              text=True, encoding="utf-8", timeout=TIMEOUT_SECONDS)
    outcome = json.loads(finished.stdout) if finished.stdout.strip().startswith("{") else {"result": finished.stdout + finished.stderr, "is_error": True}
    answer = outcome.get("result") or ""
    calls = calls_of(session_id)
    record = {
        "time": started.isoformat(timespec="seconds"),
        "label": label,
        "probe": probe["id"],
        "agent": "claude",
        "models": sorted((outcome.get("modelUsage") or {}).keys()),
        "session": session_id,
        "failed": bool(outcome.get("is_error")),
        "seconds": round((outcome.get("duration_ms") or 0) / 1000),
        "turns": outcome.get("num_turns"),
        "cost_usd": round(outcome.get("total_cost_usd") or 0, 3),
        "facts_found": [it for it in probe["facts"] if re.search(it, answer, re.IGNORECASE)],
        "facts_missing": [it for it in probe["facts"] if not re.search(it, answer, re.IGNORECASE)],
        **calls,
        "answer": answer,
    }
    with open(RUNS, "a", encoding="utf-8") as runs:
        runs.write(json.dumps(record, ensure_ascii=False) + "\n")
    return record


def calls_of(session_id):
    """What the probe's session and its subagents did, from their transcripts."""
    paths = [path for root in CLAUDE_ROOTS for pattern in (f"*/{session_id}.jsonl", f"*/{session_id}/subagents/*.jsonl")
             for path in glob.glob(os.path.join(root, pattern))]
    tools, navigation, as_text, whole_files = Counter(), 0, 0, 0
    for path in paths:
        root = next(it for it in CLAUDE_ROOTS if os.path.normcase(path).startswith(os.path.normcase(it)))
        work = SessionWork(ClaudeTranscript(root, path, set()).read())
        work.find_episodes()
        tools.update(call.tool for call in work.calls)
        navigation += sum(call.tool in ROYDER_NAVIGATION for call in work.calls)
        as_text += sum(it.kind in LOOKED_UP_AS_TEXT for it in work.episodes)
        whole_files += sum(it.kind == "whole-file-read" for it in work.episodes)
    return {"transcripts": len(paths), "royder_navigation_calls": navigation, "symbols_looked_up_as_text": as_text,
            "large_files_read_whole": whole_files, "calls_by_tool": dict(tools.most_common())}


def report():
    with open(RUNS, encoding="utf-8") as lines:
        runs = [json.loads(it) for it in lines if it.strip()]
    print("time (UTC) | label | probe | facts | cost | turns | seconds | Royder navigation | looked up as text"
          " | large files read whole | tools")
    for it in runs:
        found = len(it["facts_found"])
        tools = ", ".join(f"{tool} {count}" for tool, count in it["calls_by_tool"].items())
        print(f"{it['time'][:16]} | {it['label']} | {it['probe']} | {found}/{found + len(it['facts_missing'])}"
              f" | ${it['cost_usd']:.2f} | {it['turns']} | {it['seconds']} | {it['royder_navigation_calls']}"
              f" | {it['symbols_looked_up_as_text']} | {it['large_files_read_whole']} | {tools}"
              f"{' | FAILED' if it['failed'] else ''}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("what", choices=["run", "report"])
    parser.add_argument("--probe", help="the id of one probe; all of them when left out")
    parser.add_argument("--label", help="what is live for this run, such as Royder's and the rules' commits")
    arguments = parser.parse_args()
    if arguments.what == "report":
        report()
        return
    if not arguments.label:
        parser.error("run needs --label")
    with open(PROBES, encoding="utf-8") as file:
        probes = [it for it in json.load(file) if not arguments.probe or it["id"] == arguments.probe]
    if not probes:
        parser.error(f"no probe {arguments.probe}")
    for probe in probes:
        record = run(probe, arguments.label)
        print(f"{probe['id']}: facts {len(record['facts_found'])}/{len(probe['facts'])}, ${record['cost_usd']:.2f},"
              f" Royder navigation {record['royder_navigation_calls']}, looked up as text {record['symbols_looked_up_as_text']},"
              f" large files read whole {record['large_files_read_whole']}{', FAILED' if record['failed'] else ''}", flush=True)


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
