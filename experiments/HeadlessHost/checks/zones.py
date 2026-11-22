import json
import re

from .engine import ROOT


def check_zones(engine):
    lines = engine.console("refactorings")
    status = json.loads(next(line.removeprefix("ZONE-STATUS ") for line in lines if line.startswith("ZONE-STATUS ")))
    helpers = json.loads(next(line.removeprefix("REFACTORINGS ") for line in lines if line.startswith("REFACTORINGS ")))
    names = {"ICodeEditingZone", "ExternalSourcesZone"}
    targets = [zone for zone in status if zone["zone"] in names]
    if len(targets) != len(names) or {zone["zone"] for zone in targets} != names:
        raise RuntimeError(f"The zone catalog did not identify both features: {targets}")
    if any(zone["running"] != (zone["zone"] in helpers["zones"]) for zone in targets):
        raise RuntimeError(f"The zone report disagrees with the composed shell: {targets}")
    source = engine.call("get_symbol_source", symbolName="Newtonsoft.Json.JsonReaderException", kind="type")
    result = engine.call("get_diagnostics", filePath=str(ROOT / "src/DotNetAnalysisAndRefactoringMcp/McpShellComponent.cs"),
                         minSeverity="hint")
    diagnostics = json.loads(result)["diagnostics"]
    engine.observe("zone outcomes", {
        "requested": engine.request_zones,
        "zones": targets,
        "runningZoneCount": len(helpers["zones"]),
        "rename": helpers["rename"], "move": helpers["move"],
        "hasCompiledTypeDeclaration": bool(re.search(r"\bclass JsonReaderException\b", source)),
        "metadataView": "// metadata view of " in source,
        "quickFixes": [{key: it[key] for key in ("inspectionId", "line", "column", "hasQuickFix")}
                       for it in diagnostics],
    })
