"""Exercise the unchanged Royder plugin in a live .NET 10 host."""

import argparse
import subprocess
import sys
from contextlib import nullcontext
from datetime import datetime

sys.dont_write_bytecode = True
from checks.engine import CACHE, SOLUTION, LiveEngine
from checks.edits import check_edit
from checks.freshness import check_freshness
from checks.probe_files import MOVED, PROJECT_FILE, SOURCE, ProbeFiles


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    freshness = ["content", "add", "delete", "move-file"]
    edits = ["rename", "fix-usings", "move-type"]
    checks = ["health", *freshness, *edits]
    parser.add_argument("check", choices=[*checks, "all"])
    check = parser.parse_args().check
    if check != "health":
        subprocess.run(["dotnet", "restore", PROJECT_FILE], check=True)
    for case in checks if check == "all" else [check]:
        evidence = CACHE / "evidence" / (datetime.now().strftime("%Y%m%d-%H%M%S-%f") + "-" + case)
        evidence.mkdir(parents=True)
        print("Evidence:", evidence, flush=True)
        with ProbeFiles(case, evidence) if case != "health" else nullcontext():
            solution = SOLUTION if case == "health" else PROJECT_FILE.with_suffix(".slnx")
            with LiveEngine(evidence, solution) as engine:
                if case in freshness:
                    engine.observe("result", check_freshness(engine, case))
                    source = engine.document(SOURCE)
                    if case == "delete" and source["projectItems"]:
                        engine.call("reload_projects", filePath=str(PROJECT_FILE))
                        engine.document(SOURCE)
                    if case == "move-file":
                        engine.document(MOVED)
                elif case in edits:
                    engine.observe("edit persisted", check_edit(engine, case))


if __name__ == "__main__":
    main()
