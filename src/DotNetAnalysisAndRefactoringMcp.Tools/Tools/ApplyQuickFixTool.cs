using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.DocumentModel;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Bulbs;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Feature.Services.Intentions;
using JetBrains.ReSharper.Feature.Services.QuickFixes;
using JetBrains.ReSharper.Psi;
using JetBrains.TextControl;
using JetBrains.Util.dataStructures.TypedIntrinsics;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   Analyzes quick-fixes in the background and executes the selected bulb action on the main thread.
/// </summary>
public class ApplyQuickFixTool : IMcpAsyncTool, IMcpEditTool
{
   readonly ISolution _solution;

   public ApplyQuickFixTool(ISolution solution) => _solution = solution;

   public string Name => "apply_quick_fix";
   public ToolAnnotations Annotations => ToolAnnotations.OpenWorldChange;

   public string Description =>
      "Apply a ReSharper quick-fix (bulb action) at a position in a file. " +
      "RISKY / best-effort: quick-fix execution needs an interactive editor, so it may not " +
      "succeed headlessly. Provide filePath + line + column (1-based). " +
      "Omit 'fixId'/'index' to list the available fixes at that position. " +
      "If exactly one fix is available it is applied automatically; otherwise pass 'fixId' " +
      "(the fix's display text, from the listing) or 'index' (0-based) to choose one. " +
      "Returns { applied, fixId, file, reason?, changedFiles? }.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   filePath = new { type = "string", description = "Absolute path to the file containing the issue to fix" },
                                                   line = new { type = "integer", description = "1-based line number of the issue" },
                                                   column = new { type = "integer", description = "1-based column number of the issue" },
                                                   fixId = new { type = "string", description = "The fix to apply: its id (e.g. 'OptimizeImportsFix', as returned by list_quick_fixes and the 'available' list) or its display text. Optional." },
                                                   index = new { type = "integer", description = "0-based index into the available fixes to apply. Optional alternative to fixId." }
                                                },
                                   required = Array.Empty<string>()
                                };

   object IMcpTool.Execute(JObject arguments) =>
      throw new InvalidOperationException("Use the asynchronous tool dispatcher.");

   public Task<object> ExecuteAsync(JObject arguments, McpToolExecution execution) =>
      execution.ReadThenMain<object>(scope =>
      {
         var result = Prepare(arguments);

         return result is Func<object> apply ? scope.MainReadAction(apply) : scope.Value(result);
      });

   object Prepare(JObject arguments)
   {
      // ----- input -----
      var filePath = arguments["filePath"]?.ToString();
      var line = arguments["line"]?.Value<int>() ?? 0;
      var column = arguments["column"]?.Value<int>() ?? 0;
      var fixId = arguments["fixId"]?.ToString();
      var hasIndex = arguments["index"] != null && arguments["index"].Type != JTokenType.Null;
      var index = arguments["index"]?.Value<int>() ?? -1;

      if(string.IsNullOrEmpty(filePath) || line <= 0 || column <= 0)
         return new { error = "Provide 'filePath' + 'line' + 'column' (1-based)" };

      var sourceFile = PsiHelpers.GetSourceFile(_solution, filePath);
      if(sourceFile == null)
         return new { error = PsiHelpers.ResolveFile(_solution, filePath).Error };

      var document = sourceFile.Document;
      if(document == null)
         return new { error = "Could not get document for file" };

      // ----- compute the offset for the position -----
      var (offset, positionError) = DocumentPositions.OffsetOf(document, line, column);
      if(positionError != null)
         return new { error = positionError };

      // ----- gather candidate fixes -----
      var unreadable = new SkippedElements();
      var (candidates, incomplete) = CollectCandidates(sourceFile, offset, unreadable);

      if(candidates.Count == 0)
      {
         return new
                {
                   applied = false,
                   file = filePath,
                   reason = "No quick-fixes are available at this position.",
                   incomplete,
                   skipped = unreadable.Line,
                   available = Array.Empty<object>()
                };
      }

      var available = candidates
                     .Select((c, i) => new { index = i, fixId = c.FixTypeName, text = c.Text, highlighting = c.HighlightingId })
                     .ToList<object>();

      // ----- choose which fix to apply -----
      Candidate chosen = null;

      if(!string.IsNullOrEmpty(fixId))
      {
         // Accept both vocabularies: the fix id list_quick_fixes reports and the display text.
         chosen = candidates.FirstOrDefault(c =>
                                               string.Equals(c.FixTypeName, fixId, StringComparison.Ordinal)
                                            || string.Equals(c.Text, fixId, StringComparison.Ordinal))
               ?? candidates.FirstOrDefault(c =>
                                               string.Equals(c.FixTypeName, fixId, StringComparison.OrdinalIgnoreCase)
                                            || string.Equals(c.Text, fixId, StringComparison.OrdinalIgnoreCase));

         if(chosen == null)
            return new
                   {
                      applied = false,
                      file = filePath,
                      reason = $"No available fix matches fixId '{fixId}'.",
                      incomplete,
                      skipped = unreadable.Line,
                      available
                   };
      } else if(hasIndex)
      {
         if(index < 0 || index >= candidates.Count)
            return new
                   {
                      applied = false,
                      file = filePath,
                      reason = $"index {index} is out of range (0..{candidates.Count - 1}).",
                      available
                   };
         chosen = candidates[index];
      } else if(candidates.Count == 1)
      {
         chosen = candidates[0];
      } else
      {
         // Multiple fixes and no selection -> return the list so the caller can pick.
         return new
                {
                   applied = false,
                   file = filePath,
                   reason = $"{candidates.Count} fixes available; specify 'fixId' or 'index' to apply one.",
                   available
                };
      }

      // ----- apply the chosen fix -----
      Func<object> apply = () => ApplyFix(chosen, document, sourceFile, filePath, available);

      return apply;
   }

   /// <summary>
   /// Collects the quick-fixes (bulb actions) available at the position. Drives the real ReSharper
   /// daemon engine headlessly over the file via <see cref="DaemonHighlightingCollector"/> so the
   /// highlightings exist even when the file was never opened in an editor (the document-markup model
   /// is empty in that case). Each covering <see cref="HighlightingInfo"/> is mapped to its available
   /// quick-fixes via <see cref="QuickFixTable"/>; one candidate is produced per bulb action.
   /// </summary>
   /// <summary>
   ///   The bulb actions of the quick-fixes at <paramref name="offset"/>, and why the daemon's list may be incomplete.
   ///   A fix ReSharper fails to read costs only itself, and goes in <paramref name="unreadable"/>.
   /// </summary>
   (List<Candidate> Candidates, string Incomplete) CollectCandidates(IPsiSourceFile sourceFile, int offset, SkippedElements unreadable)
   {
      var result = new List<Candidate>();
      var seen = new HashSet<string>();

      var quickFixTable = _solution.GetComponent<QuickFixTable>();
      var collected = DaemonHighlightingCollector.Collect(_solution, sourceFile);

      foreach(var highlightingInfo in collected)
      {
         if(highlightingInfo?.Highlighting == null) continue;

         // Keep only highlightings whose range covers the requested offset.
         var range = highlightingInfo.Range;
         if(!range.IsValid()) continue;
         if(offset < range.StartOffset.Offset || offset > range.EndOffset.Offset) continue;

         var highlightingId = highlightingInfo.Highlighting.GetType().Name;
         var fixInstances = unreadable.Read(() => $"the quick-fixes of {highlightingId}",
                                            () => quickFixTable.EnumerateAvailableQuickFixes(highlightingInfo)?.ToList());
         if(fixInstances == null) continue;

         foreach(var fixInstance in fixInstances)
         {
            if(fixInstance?.QuickFix == null) continue;

            // The same id list_quick_fixes reports, so callers can pass it straight back here.
            var fixTypeName = fixInstance.QuickFix.GetType().Name;

            // Expand the quick-fix to its bulb action(s) via the stable IQuickFix.CreateBulbItems()
            // interface method. QuickFixInstance.CreateActionInstances is an unstable internal API
            // whose signature differs across Rider builds; CreateBulbItems yields the same
            // IntentionAction items it wraps.
            var bulbItems = unreadable.Read(() => $"the bulb items of {fixTypeName}", () => fixInstance.QuickFix.CreateBulbItems()?.ToList());
            if(bulbItems == null) continue;

            foreach(var bulbItem in bulbItems)
            {
               var bulbAction = bulbItem?.BulbAction;
               if(bulbAction == null) continue;

               var text = unreadable.Read(() => $"the text of {fixTypeName}", () => TextOf(bulbItem, bulbAction));
               if(string.IsNullOrEmpty(text)) continue;

               // De-dup identical fixes coming from overlapping highlightings.
               var key = highlightingId + "|" + fixTypeName + "|" + text;
               if(!seen.Add(key)) continue;

               result.Add(new Candidate
                          {
                             FixTypeName = fixTypeName,
                             Text = text,
                             HighlightingId = highlightingId,
                             BulbAction = bulbAction
                          });
            }
         }
      }

      return (result, collected.Incomplete);
   }

   object ApplyFix(
      Candidate chosen,
      IDocument document,
      IPsiSourceFile sourceFile,
      string filePath,
      List<object> available)
   {
      // Reuse an already-open text control for this document if there is one; otherwise create a synthetic,
      // short-lived control. What throws, here or in the fix, reaches the host's boundary, which answers with the
      // error and logs it.
      ITextControl textControl;
      LifetimeDefinition syntheticLifetime = null;
      var usedSyntheticControl = false;

      try
      {
         textControl = OpenTextControlOf(document);

         if(textControl == null)
         {
            syntheticLifetime = Lifetime.Define(_solution.GetSolutionLifetimes().MaximumLifetime, "DotNetAnalysisAndRefactoringMcp.ApplyQuickFix");
            textControl = _solution.GetComponent<ITextControlManager>().CreateTextControl(syntheticLifetime.Lifetime, document);
            usedSyntheticControl = textControl != null;
         }

         if(textControl == null)
            return new
                   {
                      applied = false,
                      fixId = chosen.FixTypeName,
                      text = chosen.Text,
                      file = filePath,
                      reason = "requires interactive editor / not supported headless: no text control available",
                      available
                   };

         // Snapshot the document text so we can report whether the file actually changed.
         var before = document.GetText();

         // The bulb action owns its write transactions.
         chosen.BulbAction.Execute(_solution, textControl);

         var changedFiles = new List<string>();
         if(!string.Equals(before, document.GetText(), StringComparison.Ordinal))
            changedFiles.Add(filePath);

         return new
                {
                   applied = true,
                   fixId = chosen.FixTypeName,
                   text = chosen.Text,
                   file = filePath,
                   usedSyntheticEditor = usedSyntheticControl,
                   changedFiles
                };
      }
      finally
      {
         // Always tear down a synthetic control's lifetime; never leave it dangling.
         syntheticLifetime?.Terminate();
      }
   }

   /// <summary>
   /// Finds an already-open text control whose document is the one we are fixing. Avoids
   /// opening editor tabs and is the safest source of a usable control.
   /// </summary>
   ITextControl OpenTextControlOf(IDocument document) =>
      _solution.GetComponent<ITextControlManager>().TextControls.FirstOrDefault(it => it != null && ReferenceEquals(it.Document, document));

   /// <summary>The intention's rich text, as the bulb menu shows it, or else the bulb action's.</summary>
   static string TextOf(IntentionAction intentionAction, IBulbAction bulbAction) =>
      intentionAction.RichText?.Text is { Length: > 0 } rich ? rich : bulbAction.Text;

   class Candidate
   {
      public string FixTypeName;
      public string Text;
      public string HighlightingId;
      public IBulbAction BulbAction;
   }
}
