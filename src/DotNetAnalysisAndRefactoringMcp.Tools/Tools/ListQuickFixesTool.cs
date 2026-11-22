using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Application.Settings;
using JetBrains.DocumentModel;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Feature.Services.QuickFixes;
using JetBrains.ReSharper.Psi;
using JetBrains.Util.dataStructures.TypedIntrinsics;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
/// Enumerates the ReSharper bulb actions (quick-fixes) available at a specific position in a file.
/// Runs the real daemon inspection stages headlessly to collect the highlightings covering the
/// requested offset, then asks the <see cref="QuickFixTable"/> which quick-fixes are available for
/// each of those highlightings and reports the bulb-action metadata. Read-only: it never mutates the
/// PSI or documents (<see cref="QuickFixTable.EnumerateAvailableQuickFixes(HighlightingInfo)"/>
/// asserts read access only).
/// </summary>
public class ListQuickFixesTool : IMcpTool
{
   readonly ISolution _solution;

   public ListQuickFixesTool(ISolution solution) => _solution = solution;

   public string Name => "list_quick_fixes";
   public ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

   public string Description =>
      "List the ReSharper quick-fixes (bulb actions) available at a position in a file. " +
      "Runs the daemon inspections over the file, finds the highlighting(s) covering the given " +
      "line/column, and reports the available quick-fixes for them: the fix id, the bulb-action " +
      "display text, the quick-fix .NET type, and the highlighting .NET type. Read-only.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   filePath = new
                                                              {
                                                                 type = "string",
                                                                 description = "Absolute path to the file to inspect"
                                                              },
                                                   line = new
                                                          {
                                                             type = "integer",
                                                             description = "1-based line number of the position to list quick-fixes for"
                                                          },
                                                   column = new
                                                            {
                                                               type = "integer",
                                                               description = "1-based column number of the position to list quick-fixes for"
                                                            }
                                                },
                                   required = new[] { "filePath", "line", "column" }
                                };

   public object Execute(JObject arguments)
   {
      var filePath = arguments["filePath"]?.ToString();
      if(string.IsNullOrEmpty(filePath))
         return new { error = "filePath is required" };

      var line = arguments["line"]?.Value<int>() ?? 0;
      var column = arguments["column"]?.Value<int>() ?? 0;
      if(line <= 0 || column <= 0)
         return new { error = "Provide 'filePath' plus 1-based 'line' and 'column'" };

      var sourceFile = PsiHelpers.GetSourceFile(_solution, filePath);
      if(sourceFile == null)
         return new { error = PsiHelpers.ResolveFile(_solution, filePath).Error };

      var document = sourceFile.Document;
      if(document == null)
         return new { error = "Could not get document for file" };

      var (positionOffset, positionError) = DocumentPositions.OffsetOf(document, line, column);
      if(positionError != null)
         return new { error = positionError };

      var settings = sourceFile.GetSettingsStoreWithEditorConfig(_solution);
      var quickFixTable = _solution.GetComponent<QuickFixTable>();

      // Drive the real ReSharper daemon engine headlessly over this file via the shared collector
      // (DaemonProcessBase.DoHighlighting orchestration). We are already on the R# main thread under a
      // read lock with documents committed (see McpServerComponent), which is what the daemon requires.
      var collected = DaemonHighlightingCollector.Collect(_solution, sourceFile, settings);

      // Keep only the highlightings whose range covers the requested offset.
      var atPosition = new List<HighlightingInfo>();
      foreach(var info in collected)
      {
         var range = info.Range;
         if(!range.IsValid()) continue;

         var start = range.StartOffset.Offset;
         var end = range.EndOffset.Offset;
         if(positionOffset < start || positionOffset > end) continue;

         atPosition.Add(info);
      }

      // Enumerate the available quick-fixes for each covering highlighting and shape the result. A fix ReSharper fails
      // to read costs only itself, and the answer lists it.
      var fixes = new List<Dictionary<string, object>>();
      var seenFixes = new HashSet<string>();
      var unreadable = new SkippedElements();

      foreach(var highlightingInfo in atPosition)
      {
         var highlighting = highlightingInfo.Highlighting;
         if(highlighting == null) continue;

         var highlightingType = highlighting.GetType().FullName ?? highlighting.GetType().Name;

         // QuickFixTable.EnumerateAvailableQuickFixes only requires read access and yields
         // QuickFixInstances for quick-fixes that reported IsAvailable() == true.
         var instances = unreadable.Read(() => $"the quick-fixes of {highlightingType}",
                                         () => quickFixTable.EnumerateAvailableQuickFixes(highlightingInfo)?.ToList());
         if(instances == null) continue;

         foreach(var instance in instances)
         {
            if(instance?.QuickFix == null) continue;

            var fixType = instance.QuickFix.GetType();
            var quickFixType = fixType.FullName ?? fixType.Name;
            var fixId = fixType.Name;

            // Each quick-fix expands to one or more bulb actions. Use the stable
            // IQuickFix.CreateBulbItems() interface method; QuickFixInstance.CreateActionInstances
            // is an unstable internal API whose signature differs across Rider builds.
            var bulbItems = unreadable.Read(() => $"the bulb items of {quickFixType}", () => instance.QuickFix.CreateBulbItems()?.ToList());
            if(bulbItems == null) continue;

            foreach(var bulbItem in bulbItems)
            {
               if(bulbItem == null) continue;

               var text = unreadable.Read(() => $"the text of {quickFixType}", () => bulbItem.BulbAction?.Text);

               // Deduplicate identical fixes (same highlighting + fix type + text).
               var dedupKey = $"{highlightingType}|{quickFixType}|{text}";
               if(!seenFixes.Add(dedupKey)) continue;

               fixes.Add(new Dictionary<string, object>
                         {
                            ["fixId"] = fixId,
                            ["text"] = text,
                            ["quickFixType"] = quickFixType,
                            ["highlightingType"] = highlightingType
                         });
            }
         }
      }

      var ordered = fixes
                   .OrderBy(f => (string)f["text"] ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                   .ToList();

      var result = new Dictionary<string, object>
                   {
                      ["file"] = filePath,
                      ["line"] = line,
                      ["column"] = column,
                      ["fixes"] = ordered
                   };

      if(collected.Incomplete != null)
         result["incomplete"] = collected.Incomplete;
      if(unreadable.Line != null)
         result["skipped"] = unreadable.Line;

      if(collected.Count == 0)
         result["note"] = "The daemon's stages produced no highlightings for this file.";
      else if(atPosition.Count == 0)
         result["note"] =
            $"{collected.Count} highlighting(s) were produced for this file but none cover {line}:{column}.";
      else if(ordered.Count == 0)
         result["note"] =
            $"Highlighting(s) cover {line}:{column} but no quick-fixes are available for them.";

      return result;
   }
}
