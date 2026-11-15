using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Application.Settings;
using JetBrains.DocumentModel;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi;
using JetBrains.Util.dataStructures.TypedIntrinsics;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
/// Runs the real ReSharper daemon inspection stages headlessly over a single file and reports
/// the resulting highlightings (severity, inspection id, message, quick-fix availability).
/// This is richer than <c>get_file_errors</c> (which only walks the PSI tree for syntax errors
/// and unresolved references) because it surfaces actual code-analysis inspections.
/// Read-only: it never mutates the PSI or documents.
/// </summary>
public class GetDiagnosticsTool : IMcpTool
{
   readonly ISolution _solution;

   public GetDiagnosticsTool(ISolution solution) => _solution = solution;

   public string Name => "get_diagnostics";

   public string Description =>
      "Run ReSharper's daemon inspections on a file and report diagnostics: severity, inspection id, " +
      "message, location, and whether a quick-fix is available. Richer than get_file_errors " +
      "(which only finds syntax errors and unresolved references). " +
      "Filter with minSeverity ('error'|'warning'|'suggestion'|'hint', default 'warning'). " +
      "Pass an optional position {line, column} to keep only the diagnostics covering that spot " +
      "(useful for explaining the error/warning under the cursor).";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   filePath = new
                                                              {
                                                                 type = "string",
                                                                 description = "Absolute path to the file to analyze"
                                                              },
                                                   minSeverity = new
                                                                 {
                                                                    type = "string",
                                                                    description = "Minimum severity to report: 'error', 'warning', 'suggestion', or 'hint'. Default: 'warning'."
                                                                 },
                                                   position = new
                                                              {
                                                                 type = "object",
                                                                 description = "Optional position. When set, only diagnostics whose range covers this offset are returned (explain-error mode).",
                                                                 properties = new
                                                                              {
                                                                                 line = new { type = "integer", description = "1-based line number" },
                                                                                 column = new { type = "integer", description = "1-based column number" }
                                                                              }
                                                              }
                                                },
                                   required = new[] { "filePath" }
                                };

   public object Execute(JObject arguments)
   {
      var filePath = arguments["filePath"]?.ToString();
      if(string.IsNullOrEmpty(filePath))
         return new { error = "filePath is required" };

      var minSeverity = Severities.Parse(arguments["minSeverity"]?.ToString()) ?? Severity.WARNING;

      // Optional position (explain-error mode)
      int posLine = 0, posColumn = 0;
      var hasPosition = false;
      var positionToken = arguments["position"] as JObject;
      if(positionToken != null)
      {
         posLine = positionToken["line"]?.Value<int>() ?? 0;
         posColumn = positionToken["column"]?.Value<int>() ?? 0;
         hasPosition = posLine > 0 && posColumn > 0;
      }

      var sourceFile = PsiHelpers.GetSourceFile(_solution, filePath);
      if(sourceFile == null)
         return new { error = PsiHelpers.ResolveFile(_solution, filePath).Error };

      // Resolve the document offset for position filtering, if requested.
      int? positionOffset = null;
      if(hasPosition && sourceFile.Document is {} document)
      {
         var (offset, positionError) = DocumentPositions.OffsetOf(document, posLine, posColumn);
         if(positionError != null)
            return new { error = positionError };
         positionOffset = offset;
      }

      var settings = sourceFile.GetSettingsStoreWithEditorConfig(_solution);
      var mgr = _solution.GetComponent<HighlightingSettingsManager>();

      // Drive the real ReSharper daemon engine headlessly over this file via the shared collector
      // (DaemonProcessBase.DoHighlighting orchestration). We are already on the R# main thread under a
      // read lock with documents committed (see McpServerComponent), which is what the daemon requires.
      var collected = DaemonHighlightingCollector.Collect(_solution, sourceFile, settings);

      // Shape the diagnostics. A highlighting ReSharper fails to read costs only itself, or only the value it failed
      // on, and the answer lists it.
      var diagnostics = new List<Dictionary<string, object>>();
      var unreadable = new SkippedElements();
      foreach(var info in collected)
      {
         var highlighting = info.Highlighting;
         if(highlighting == null) continue;

         var name = highlighting.GetType().Name;
         unreadable.Guard(() => name,
                          () =>
                          {
                             var severity = mgr.GetSeverity(highlighting, sourceFile, _solution, settings);
                             if(!Severities.AtLeast(severity, minSeverity)) return;

                             var range = info.Range;
                             if(!range.IsValid()) return;

                             // Position filtering (explain-error mode): keep only diagnostics covering the offset.
                             if(positionOffset.HasValue && (positionOffset.Value < range.StartOffset.Offset || positionOffset.Value > range.EndOffset.Offset))
                                return;

                             var (startLine, startCol) = PsiHelpers.GetLineColumn(range.StartOffset);
                             var (endLine, endCol) = PsiHelpers.GetLineColumn(range.EndOffset);

                             // CouldHavePopupQuickFix(IHighlighting) is an unreliable false-negative headless: it reported no fix for
                             // a NotAccessedField.Local warning that list_quick_fixes found 3 real fixes for. So the QuickFixTable
                             // enumerates the fixes, as list_quick_fixes does.
                             diagnostics.Add(new Dictionary<string, object>
                                             {
                                                ["inspectionId"] = highlighting.GetConfigurableSeverityId() ?? name,
                                                ["severity"] = Severities.Name(severity),
                                                ["message"] = unreadable.Read(() => $"the message of {name}", () => highlighting.ToolTip ?? highlighting.ErrorStripeToolTip),
                                                ["line"] = startLine,
                                                ["column"] = startCol,
                                                ["endLine"] = endLine,
                                                ["endColumn"] = endCol,
                                                ["hasQuickFix"] = unreadable.Read(() => $"the quick-fixes of {name}", () => DaemonHighlightingCollector.HasQuickFix(_solution, info))
                                             });
                          });
      }

      // Stable, predictable ordering: by location.
      var ordered = diagnostics
                   .OrderBy(d => (int)d["line"])
                   .ThenBy(d => (int)d["column"])
                   .ToList();

      var result = new Dictionary<string, object>
                   {
                      ["file"] = filePath,
                      ["diagnosticsCount"] = ordered.Count,
                      ["diagnostics"] = ordered
                   };

      if(collected.Incomplete != null)
         result["incomplete"] = collected.Incomplete;
      if(unreadable.Line != null)
         result["skipped"] = unreadable.Line;

      if(collected.Count == 0)
         result["note"] = "The daemon's stages produced no highlightings for this file.";
      else if(ordered.Count == 0)
         result["note"] =
            $"{collected.Count} highlighting(s) were produced but none met the minimum severity " +
            $"'{Severities.Name(minSeverity)}'" +
            (positionOffset.HasValue ? " (or covered the requested position)." : ".");

      return result;
   }
}
