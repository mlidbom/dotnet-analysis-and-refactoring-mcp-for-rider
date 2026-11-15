using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Application;
using JetBrains.Application.Changes;
using JetBrains.Application.Threading;
using JetBrains.Application.Progress;
using JetBrains.DocumentModel;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Bulbs;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Feature.Services.Intentions.Scoped.Actions;
using JetBrains.ReSharper.Feature.Services.Intentions.Scoped.Scopes;
using JetBrains.ReSharper.Feature.Services.QuickFixes;
using JetBrains.ReSharper.Psi;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
/// Applies ReSharper inspection quick-fixes across a whole file headlessly, by inspection id —
/// e.g. converting every explicit constructor into a primary constructor. Drives ReSharper's own
/// "Fix all in file" engine: for each chosen inspection it runs the scoped quick-fix, which rewrites
/// every occurrence and re-analyzes until stable.
///
/// Complements <see cref="ApplyQuickFixTool"/> (apply_quick_fix), which applies a single bulb action at a
/// position. This tool is position-free and file-wide. Only scoped fixes (<see cref="IModernManualScopedAction"/>)
/// can run this way; non-scoped fixes are reported as skipped — use apply_quick_fix for those.
///
/// Self-transacting: the scoped executor manages its own PSI transactions.
/// Shares <see cref="DaemonHighlightingCollector"/> with get_diagnostics / list_quick_fixes.
/// </summary>
public class ApplySuggestionsTool : IMcpAsyncTool, IMcpEditTool
{
   sealed class Pass
   {
      public string TypeName;
      public string Applied;
      public string Error;
      public readonly HashSet<string> Skipped = new();
   }

   // Upper bound on distinct fix types applied per file — a safety net against a fix that never clears its highlighting.
   const int MaxFixTypesPerFile = 50;

   readonly ISolution _solution;

   public ApplySuggestionsTool(ISolution solution) => _solution = solution;

   public string Name => "apply_suggestions";

   public string Description =>
      "Apply ReSharper's quick-fixes by inspection id across a file, or its folder, project or the whole " +
      "solution ('scope'), as Rider's Fix in file/folder/project/solution does (e.g. remove every redundant " +
      "using). Complements apply_quick_fix, which applies one fix at a specific position. Run get_diagnostics " +
      "or inspect_code first to discover inspection ids. Specify which to apply via 'inspectionIds', or pass all=true to apply every applicable " +
      "suggestion. Each chosen fix rewrites all its occurrences and re-analyzes until stable. Only headlessly-" +
      "applicable (scoped) fixes are applied; others are reported as skipped. Pass dryRun=true to preview " +
      "without modifying the file. Pass multiple files via the 'filePaths' array.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   filePath = new
                                                              {
                                                                 type = "string",
                                                                 description = "Absolute path to the file to apply suggestions to"
                                                              },
                                                   filePaths = new
                                                               {
                                                                  type = "array",
                                                                  description = "Array of absolute file paths to process in batch. Results are concatenated with separators. Alternative to single 'filePath' parameter.",
                                                                  items = new { type = "string" }
                                                               },
                                                   inspectionIds = new
                                                                   {
                                                                      type = "string",
                                                                      description = "Comma-separated inspection ids to apply (e.g. 'ConvertToPrimaryConstructor'). Use get_diagnostics to discover ids."
                                                                   },
                                                   all = new
                                                         {
                                                            type = "boolean",
                                                            description = "Apply every applicable (scoped) suggestion in the file. Ignored when 'inspectionIds' is set. Default: false."
                                                         },
                                                   dryRun = new
                                                            {
                                                               type = "boolean",
                                                               description = "Report what would be applied without modifying the file. Default: false."
                                                            },
                                                   scope = new
                                                           {
                                                              type = "string",
                                                              description = "How far each fix is applied: 'file' (default), or the file's 'folder', 'project' or the " +
                                                                            "whole 'solution', as Rider's Fix in folder/project/solution. The file must have an " +
                                                                            "occurrence: it's where the fix is found. inspect_code finds such files."
                                                           }
                                                },
                                   required = new string[0]
                                };

   object IMcpTool.Execute(JObject arguments) =>
      throw new InvalidOperationException("Use the asynchronous tool dispatcher.");

   public async Task<object> ExecuteAsync(JObject arguments, McpToolExecution execution)
   {
      var filePathsToken = arguments["filePaths"] as JArray;
      if(filePathsToken != null && filePathsToken.Count > 0)
      {
         var sb = new StringBuilder();
         for(var i = 0; i < filePathsToken.Count; i++)
         {
            if(i > 0) sb.AppendLine().AppendLine();
            var itemArgs = new JObject { ["filePath"] = filePathsToken[i]?.ToString() };
            CopyIfPresent(arguments, itemArgs, "inspectionIds");
            CopyIfPresent(arguments, itemArgs, "all");
            CopyIfPresent(arguments, itemArgs, "dryRun");
            CopyIfPresent(arguments, itemArgs, "scope");

            sb.Append("=== [").Append(i + 1).Append('/').Append(filePathsToken.Count)
              .Append("] ").Append(filePathsToken[i]).Append(" ===").AppendLine();
            sb.Append(ResultToString(await ExecuteSingleAsync(itemArgs, execution).ConfigureAwait(false)));
         }

         return sb.ToString().TrimEnd();
      }

      return await ExecuteSingleAsync(arguments, execution).ConfigureAwait(false);
   }

   async Task<object> ExecuteSingleAsync(JObject arguments, McpToolExecution execution)
   {
      var filePath = arguments["filePath"]?.ToString();
      if(string.IsNullOrEmpty(filePath))
         return new { error = "filePath is required" };

      var idFilter = ParseCsv(arguments["inspectionIds"]?.ToString());
      var applyAll = arguments["all"]?.Value<bool>() ?? false;
      var dryRun = arguments["dryRun"]?.Value<bool>() ?? false;
      var scopeName = arguments["scope"]?.ToString()?.Trim().ToLowerInvariant() ?? "file";
      if(scopeName is not ("file" or "folder" or "project" or "solution"))
         return new { error = $"scope is 'file', 'folder', 'project' or 'solution', not '{scopeName}'" };

      if(idFilter == null && !applyAll)
         return await execution.Read(() => ListApplicable(filePath)).ConfigureAwait(false);

      if(dryRun)
         return await execution.Read<object>(() =>
         {
            var sourceFile = PsiHelpers.GetSourceFile(_solution, filePath);
            if(sourceFile == null)
               return new { error = PsiHelpers.ResolveFile(_solution, filePath).Error };

            return DescribeDryRun(filePath,
                                  sourceFile,
                                  _solution.GetComponent<HighlightingSettingsManager>(),
                                  _solution.GetComponent<QuickFixTable>(),
                                  idFilter,
                                  applyAll);
         }).ConfigureAwait(false);

      var applied = new List<string>();
      var skipped = new HashSet<string>();
      var errors = new List<string>();
      var handledTypes = new HashSet<string>();

      for(var iteration = 0; iteration < MaxFixTypesPerFile; iteration++)
      {
         var pass = await execution.ReadThenMain<Pass>(Prepare).ConfigureAwait(false);
         skipped.UnionWith(pass.Skipped);
         if(pass.Error != null)
            errors.Add(pass.Error);
         if(pass.TypeName == null)
            break;

         handledTypes.Add(pass.TypeName);
         if(pass.Applied != null)
            applied.Add(pass.Applied);
      }

      return FormatResult(filePath, scopeName, applied, skipped, errors);

      ReadAndWriteScope.ReadResult<Pass> Prepare(ReadAndWriteScope scope)
      {
         var sourceFile = PsiHelpers.GetSourceFile(_solution, filePath);
         if(sourceFile == null)
            return scope.Value(new Pass { Error = PsiHelpers.ResolveFile(_solution, filePath).Error });

         var settingsManager = _solution.GetComponent<HighlightingSettingsManager>();
         var quickFixTable = _solution.GetComponent<QuickFixTable>();
         var pass = new Pass();
         foreach(var info in DaemonHighlightingCollector.Collect(_solution, sourceFile))
         {
            Interruption.Current.CheckAndThrow();
            var inspectionId = GetInspectionId(settingsManager, info.Highlighting);
            if(!Matches(inspectionId, idFilter, applyAll))
               continue;

            foreach(var instance in EnumerateFixes(quickFixTable, info))
            {
               if(instance.QuickFix is not IModernManualScopedAction scoped)
               {
                  if(inspectionId != null)
                     pass.Skipped.Add(inspectionId);
                  continue;
               }

               var typeName = instance.QuickFix.GetType().FullName;
               if(handledTypes.Contains(typeName))
                  continue;

               var text = FixText(instance);
               return scope.MainReadAction(() =>
               {
                  // Scoped actions manage their own writes and may run analysis between transactions.
                  pass.TypeName = typeName;
                  try
                  {
                     var fixScope = ScopeOf(sourceFile, scopeName);
                     var documents = DocumentStamps.Of(fixScope);
                     scoped.ExecuteAction(_solution, fixScope, info.Highlighting, NullProgressIndicator.Create());
                     _solution.GetPsiServices().Caches.Update();
                     pass.Applied = $"{inspectionId ?? "(no id)"} — \"{text}\": {Changed(documents.ChangedCount, scopeName)}";
                  }
                  catch(Exception exception)
                  {
                     pass.Error = $"{inspectionId ?? "(no id)"}: {exception.Message}";
                  }

                  return pass;
               });
            }
         }

         return scope.Value(pass);
      }
   }

   // No filter given — list the applicable inspection ids so the caller can choose.
   object ListApplicable(string filePath)
   {
      var sourceFile = PsiHelpers.GetSourceFile(_solution, filePath);
      if(sourceFile == null)
         return new { error = PsiHelpers.ResolveFile(_solution, filePath).Error };

      var settingsManager = _solution.GetComponent<HighlightingSettingsManager>();
      var quickFixTable = _solution.GetComponent<QuickFixTable>();
      var ids = ApplicableIds(sourceFile, settingsManager, quickFixTable);

      var sb = new StringBuilder();
      sb.Append(filePath).AppendLine(" — specify 'inspectionIds' or pass all=true.");
      if(ids.Count > 0)
      {
         sb.AppendLine().AppendLine("applicable inspection ids in this file:");
         foreach(var id in ids.OrderBy(x => x))
            sb.Append("  ").AppendLine(id);
      } else
      {
         sb.AppendLine().AppendLine("no applicable (scoped) suggestions found.");
      }

      return sb.ToString().TrimEnd();
   }

   object DescribeDryRun(
      string filePath,
      IPsiSourceFile sourceFile,
      HighlightingSettingsManager settingsManager,
      QuickFixTable quickFixTable,
      HashSet<string> idFilter,
      bool applyAll)
   {
      var wouldApply = new List<string>();
      var seenTypes = new HashSet<string>();
      foreach(var info in DaemonHighlightingCollector.Collect(_solution, sourceFile))
      {
         var inspectionId = GetInspectionId(settingsManager, info.Highlighting);
         if(!Matches(inspectionId, idFilter, applyAll))
            continue;

         foreach(var instance in EnumerateFixes(quickFixTable, info))
         {
            if(!(instance.QuickFix is IModernManualScopedAction))
               continue;
            if(!seenTypes.Add(instance.QuickFix.GetType().FullName))
               continue;
            wouldApply.Add($"{inspectionId ?? "(no id)"} — \"{FixText(instance)}\"");
         }
      }

      if(wouldApply.Count == 0)
         return $"{filePath} — dry run: nothing to apply";

      var sb = new StringBuilder();
      sb.Append(filePath).Append(" — dry run: would apply ").Append(wouldApply.Count).AppendLine(" fix type(s):");
      foreach(var entry in wouldApply)
         sb.Append("  ").AppendLine(entry);
      return sb.ToString().TrimEnd();
   }

   HashSet<string> ApplicableIds(
      IPsiSourceFile sourceFile,
      HighlightingSettingsManager settingsManager,
      QuickFixTable quickFixTable)
   {
      var ids = new HashSet<string>();
      foreach(var info in DaemonHighlightingCollector.Collect(_solution, sourceFile))
      {
         var inspectionId = GetInspectionId(settingsManager, info.Highlighting);
         if(inspectionId == null)
            continue;
         foreach(var instance in EnumerateFixes(quickFixTable, info))
         {
            if(instance.QuickFix is IModernManualScopedAction)
            {
               ids.Add(inspectionId);
               break;
            }
         }
      }

      return ids;
   }

   static IEnumerable<QuickFixInstance> EnumerateFixes(QuickFixTable quickFixTable, HighlightingInfo info)
   {
      if(info?.Highlighting == null)
         return Enumerable.Empty<QuickFixInstance>();
      try
      {
         var instances = quickFixTable.EnumerateAvailableQuickFixes(info);
         return instances == null ? Enumerable.Empty<QuickFixInstance>() : instances.Where(i => i?.QuickFix != null).ToList();
      }
      catch(Exception exception) when(exception is not OperationCanceledException)
      {
         // A fix's availability check can throw on unusual highlightings — treat as no fixes.
         return Enumerable.Empty<QuickFixInstance>();
      }
   }

   // Configurable inspection id (e.g. "ConvertToPrimaryConstructor"), or null if the highlighting has none.
   static string GetInspectionId(HighlightingSettingsManager settingsManager, IHighlighting highlighting)
   {
      if(highlighting == null)
         return null;
      if(highlighting is ICustomConfigurableSeverityIdHighlighting custom &&
         !string.IsNullOrEmpty(custom.ConfigurableSeverityId))
         return custom.ConfigurableSeverityId;

      var attribute = settingsManager.GetHighlightingAttribute(highlighting);
      return (attribute as ConfigurableSeverityHighlightingAttribute)?.ConfigurableSeverityId;
   }

   static bool Matches(string inspectionId, HashSet<string> idFilter, bool applyAll)
   {
      if(applyAll)
         return true;
      return inspectionId != null && idFilter.Contains(inspectionId);
   }

   static string FixText(QuickFixInstance instance)
   {
      if(instance.QuickFix is IBulbAction bulb && !string.IsNullOrEmpty(bulb.Text))
         return bulb.Text;
      return instance.QuickFix.GetType().Name;
   }

   /// <summary>
   /// Where a fix found in <paramref name="sourceFile"/> is applied: the file, its folder, its project or the
   /// solution, as Rider's Fix in file/folder/project/solution. A file outside the project model can only be fixed
   /// in itself.
   /// </summary>
   Scope ScopeOf(IPsiSourceFile sourceFile, string scopeName)
   {
      var projectFile = sourceFile.ToProjectFile();
      return scopeName switch
      {
         "folder" when projectFile?.ParentFolder is {} folder   => new ProjectFolderScope(folder, _ => true),
         "project" when projectFile?.GetProject() is {} project => new ProjectFolderScope(project, _ => true),
         "solution" when projectFile != null                    => new SolutionScope(_solution, projectFile, _ => true),
         _                                                      => new SourceFileScope(sourceFile)
      };
   }

   /// <summary>
   ///   How many files a fix changed. None at a wider scope is the trap Rider's own Fix in solution has too: the fix
   ///   skips every file in which solution-wide analysis found no such issue, and those results lag a change of
   ///   settings (seen 2026-11-15: after a new .editorconfig made <c>private</c> redundant, every file was skipped,
   ///   the one the fix was found in too).
   /// </summary>
   static string Changed(int files, string scopeName) =>
      files switch
      {
         0 when scopeName != "file" => "changed no file. A fix in a wider scope skips the files in which Rider's " +
                                       "solution-wide analysis found no such issue, and those results lag a change " +
                                       "of settings, such as an .editorconfig: scope 'file', with filePaths, works meanwhile",
         0 => "changed no file",
         1 => "changed 1 file",
         _ => $"changed {files} files"
      };

   /// <summary>The modification stamps of a scope's documents, to tell how many of them a fix changed.</summary>
   sealed class DocumentStamps
   {
      readonly List<(IDocument Document, ModificationStamp Stamp)> _before;

      DocumentStamps(List<(IDocument Document, ModificationStamp Stamp)> before) => _before = before;

      public static DocumentStamps Of(Scope scope)
      {
         var before = new List<(IDocument Document, ModificationStamp Stamp)>();
         foreach(var file in scope.GetSourceFilesToProcess(NullProgressIndicator.Create()))
            before.Add((file.Document, file.Document.LastModificationStamp));
         return new DocumentStamps(before);
      }

      public int ChangedCount => _before.Count(it => !it.Document.LastModificationStamp.Equals(it.Stamp));
   }

   static object FormatResult(string filePath, string scopeName, List<string> applied, HashSet<string> skipped, List<string> errors)
   {
      var sb = new StringBuilder();
      sb.Append(filePath).Append(" — applied ").Append(applied.Count).Append(" fix type(s)");
      sb.AppendLine(scopeName == "file" ? "" : $", each in the file's {scopeName}");

      if(applied.Count > 0)
      {
         sb.AppendLine();
         foreach(var entry in applied)
            sb.Append("  ✓ ").AppendLine(entry);
      }

      if(skipped.Count > 0)
      {
         sb.AppendLine();
         sb.AppendLine("skipped (not headlessly applicable — try apply_quick_fix at a position):");
         foreach(var id in skipped.OrderBy(x => x))
            sb.Append("  ").AppendLine(id);
      }

      if(errors.Count > 0)
      {
         sb.AppendLine();
         sb.AppendLine("errors:");
         foreach(var error in errors)
            sb.Append("  ").AppendLine(error);
      }

      return sb.ToString().TrimEnd();
   }

   static HashSet<string> ParseCsv(string csv)
   {
      if(string.IsNullOrWhiteSpace(csv))
         return null;
      return new HashSet<string>(
         csv.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
         StringComparer.OrdinalIgnoreCase);
   }

   static void CopyIfPresent(JObject source, JObject target, string key)
   {
      var token = source[key];
      if(token != null) target[key] = token;
   }

   static string ResultToString(object result)
   {
      if(result is string s) return s;
      var jo = (JObject)JsonOf.Value(result);
      return "error: " + (jo["error"]?.ToString() ?? result.ToString());
   }
}
