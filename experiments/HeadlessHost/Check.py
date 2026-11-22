"""Exercise the unchanged Royder plugin in a live .NET 10 host."""

import argparse
import subprocess
import sys
from contextlib import nullcontext
from datetime import datetime

sys.dont_write_bytecode = True
from checks.engine import CACHE, RIDER_PORT, SOLUTION, LiveEngine
from checks.edits import check_edit
from checks.freshness import check_freshness, finds_change
from checks.probe_files import FIXTURE_PROJECT, PAIRED_PROJECT, ProbeFiles
from checks.zones import check_zones


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    freshness = ["content", "add", "delete", "move-file"]
    edits = ["rename", "fix-usings", "move-type"]
    checks = ["health", *freshness, *edits]
    parser.add_argument("check", choices=[*checks, "zones", "all"])
    parser.add_argument("--paired", action="store_true", help="Use the checkout solution and verify both ReSharper backends.")
    parser.add_argument("--request-zones", choices=["editing", "external-sources", "both"],
                        help="Ask the platform's zone activator for additional shell zones.")
    args = parser.parse_args()
    check = args.check
    if args.request_zones:
        checks.insert(1, "zones")
    project = PAIRED_PROJECT if args.paired else FIXTURE_PROJECT
    if not args.paired and check not in {"health", "zones"}:
        subprocess.run(["dotnet", "restore", project], check=True)
    for case in checks if check == "all" else [check]:
        label = "-".join(str(part) for part in (args.request_zones, "paired" if args.paired else None, case) if part)
        evidence = CACHE / "evidence" / (datetime.now().strftime("%Y%m%d-%H%M%S-%f") + "-" + label)
        evidence.mkdir(parents=True)
        print("Evidence:", evidence, flush=True)
        needs_fixture = case in freshness or case in edits
        with ProbeFiles(case, evidence, project).installed() if needs_fixture else nullcontext() as files:
            solution = SOLUTION if args.paired or not needs_fixture else project.with_suffix(".slnx")
            with LiveEngine(evidence, solution, probe_missing_import=case == "fix-usings", request_zones=args.request_zones) as engine:
                if case == "zones":
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
