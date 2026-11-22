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


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    freshness = ["content", "add", "delete", "move-file"]
    edits = ["rename", "fix-usings", "move-type"]
    checks = ["health", *freshness, *edits]
    parser.add_argument("check", choices=[*checks, "all"])
    parser.add_argument("--paired", action="store_true", help="Use the checkout solution and verify both ReSharper backends.")
    args = parser.parse_args()
    check = args.check
    project = PAIRED_PROJECT if args.paired else FIXTURE_PROJECT
    if not args.paired and check != "health":
        subprocess.run(["dotnet", "restore", project], check=True)
    for case in checks if check == "all" else [check]:
        evidence = CACHE / "evidence" / (datetime.now().strftime("%Y%m%d-%H%M%S-%f") + ("-paired-" if args.paired else "-") + case)
        evidence.mkdir(parents=True)
        print("Evidence:", evidence, flush=True)
        with ProbeFiles(case, evidence, project).installed() if case != "health" else nullcontext() as files:
            solution = SOLUTION if args.paired or case == "health" else project.with_suffix(".slnx")
            with LiveEngine(evidence, solution, probe_missing_import=case == "fix-usings") as engine:
                if case in freshness:
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
