"""Exercise the unchanged Royder plugin in a live .NET 10 host."""

import argparse
import subprocess
import sys
from contextlib import nullcontext
from datetime import datetime

sys.dont_write_bytecode = True
from checks.engine import CACHE, RIDER_PORT, SOLUTION, LiveEngine
from checks.edits import check_edit
from checks import editing_services
from checks.daemon import compare_daemons
from checks.freshness import check_freshness, finds_change
from checks.probe_files import FIXTURE_PROJECT, PAIRED_PROJECT, ProbeFiles
from checks.zones import check_zones


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    freshness = ["content", "add", "delete", "move-file"]
    edits = ["rename", "fix-usings", "move-type"]
    checks = ["health", *freshness, *edits]
    removal = {"worktree-removal", "remove-indexing", "remove-idle"}
    parser.add_argument("check", choices=sorted(set(checks) | set(editing_services.CASES) | removal | {"zones", "all", "editing-services", "daemon"}))
    parser.add_argument("--paired", action="store_true", help="Use the checkout solution and verify both ReSharper backends.")
    parser.add_argument("--request-zones", choices=["editing", "external-sources", "both"],
                        help="Ask the platform's zone activator for additional shell zones.")
    parser.add_argument("--live-daemon", action="store_true", help="Use the platform's editable daemon and daemon thread.")
    parser.add_argument("--no-swea", action="store_true", help="Request local inspections only in the command-line settings.")
    args = parser.parse_args()
    check = args.check
    if check in removal:
        from checks.worktree_removal import check_removal
        if args.paired:
            parser.error("Worktree removal uses private disposable worktrees.")
        phases = ("idle", "indexing") if check == "worktree-removal" else (check.removeprefix("remove-"),)
        for phase in phases:
            check_removal(phase, args.request_zones, args.live_daemon, args.no_swea)
        return
    service_matrix = check == "editing-services" or (check in editing_services.CASES and (check not in edits or args.live_daemon))
    if (service_matrix or check == "daemon") and args.paired:
        parser.error("The editing-service and daemon experiments use only the experiment fixture.")
    if args.request_zones:
        checks.insert(1, "zones")
    project = PAIRED_PROJECT if args.paired else FIXTURE_PROJECT
    if not args.paired and check not in {"health", "zones"}:
        subprocess.run(["dotnet", "restore", project], check=True)
    cases = editing_services.CASES if check == "editing-services" else (checks if check == "all" else [check])
    for case in cases:
        label = "-".join(str(part) for part in (args.request_zones, "no-swea" if args.no_swea else None, "live-daemon" if args.live_daemon else None,
                                                "paired" if args.paired else None, case) if part)
        evidence = CACHE / "evidence" / (datetime.now().strftime("%Y%m%d-%H%M%S-%f") + "-" + label)
        evidence.mkdir(parents=True)
        print("Evidence:", evidence, flush=True)
        if case == "daemon":
            compare_daemons(evidence, args.request_zones, args.no_swea)
            continue
        needs_fixture = case in freshness or case in edits or service_matrix
        with ProbeFiles(case, evidence, project).installed() if needs_fixture else nullcontext() as files:
            solution = SOLUTION if args.paired or not needs_fixture else project.with_suffix(".slnx")
            with LiveEngine(evidence, solution, probe_missing_import=case == "fix-usings",
                            request_zones=args.request_zones, live_daemon=args.live_daemon, no_swea=args.no_swea) as engine:
                if service_matrix:
                    editing_services.check_service(engine, files)
                elif case == "zones":
                    check_zones(engine)
                elif case in freshness:
                    engine.observe("result", check_freshness(engine, case, files))
                    source = engine.document(files.source)
                    if case == "delete" and source["projectItems"]:
                        engine.call("reload_projects", filePath=str(project))
                        engine.document(files.source)
                    if case == "move-file":
                        engine.document(files.moved)
                    if args.paired:
                        engine.observe("Rider backend sees the change after the headless checks",
                                       finds_change(engine, case, files, port=RIDER_PORT))
                elif case in edits:
                    engine.observe("edit persisted", check_edit(engine, case, files))


if __name__ == "__main__":
    main()
