using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Application.Progress;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Refactorings;
using JetBrains.ReSharper.Feature.Services.Refactorings.Conflicts;
using JetBrains.ReSharper.Feature.Services.Refactorings.Specific.Rename;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Transactions;
using JetBrains.ReSharper.Refactorings.Rename;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
/// Safe, semantic, solution-wide rename. It runs as Rider runs its Rename (<see cref="IMcpMainThreadTool"/>): on the
/// R# main thread, under a read lock, documents committed, in a PSI transaction of its own, so that it controls
/// whether the transaction commits (a real rename) or is discarded (a dry-run).
///
/// The actual rename is driven by the standard ReSharper rename machinery:
///   RenameWorkflow + RenameRefactoring (the high-level executer) over a RenameDataModel.
/// The data model is populated via <c>LoadBaseRenames</c> (which builds the atomic renames AND
/// searches the solution for usages — this is what makes the rename semantic, not textual) and
/// then <c>RenameRefactoring.Execute</c> performs declaration + usage rebinding.
///
/// Conflicts are collected through a <see cref="RefactoringDriverWithConflicts"/> (the plain
/// RefactoringDriver discards conflicts), and the driver-collected <see cref="IConflict"/>s are
/// shaped into the output.
/// </summary>
public class RenameSymbolTool : IMcpMainThreadTool, IMcpEditTool
{
   readonly ISolution _solution;

   public RenameSymbolTool(ISolution solution) => _solution = solution;

   public string Name => "rename_symbol";
   public ToolAnnotations Annotations => ToolAnnotations.SolutionEdit;

   public string Description =>
      "Safely rename a code symbol (class, method, property, field, parameter, local, type parameter, etc.) " +
      "and all of its references across the entire solution. This is a semantic rename: it " +
      "updates real usages, not text matches. It follows what Rider's Rename follows: a member's interface " +
      "implementations and overrides; the doc comments' <see cref>, <paramref> and <typeparamref> tags that name the " +
      "symbol; a positional record's property with its parameter; and a type's file named after it, which is renamed " +
      "with it. Provide either a symbolName (e.g. 'MyClass' or 'Namespace.MyClass') or a file path with position " +
      "(line/column), plus the required 'newName'. Returns any conflicts the refactoring detected, and the files it " +
      "changed, saved before it answers. Set 'dryRun' to true to preview conflicts and affected files WITHOUT " +
      "modifying any code or renaming any file. A rename with an error conflict, such as a usage ReSharper can't " +
      "update correctly, is not applied unless 'applyDespiteConflicts' is true.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   symbolName = new { type = "string", description = "Symbol name to rename (e.g. 'MyClass', 'Namespace.MyClass', 'MyClass.MyMethod'). Alternative to filePath+line+column." },
                                                   kind = new { type = "string", description = "Filter by symbol kind when using symbolName: 'type', 'method', 'property', 'field', 'event'. Helps disambiguate when multiple symbols share a name." },
                                                   filePath = new { type = "string", description = "Absolute path to the file containing the symbol" },
                                                   line = new { type = "integer", description = "1-based line number of the symbol" },
                                                   column = new { type = "integer", description = "1-based column number of the symbol" },
                                                   newName = new { type = "string", description = "The new name for the symbol (required)." },
                                                   dryRun = new { type = "boolean", description = "When true, detect conflicts and report affected files WITHOUT applying any changes. Default: false." },
                                                   applyDespiteConflicts = new { type = "boolean", description = "Apply the rename even when ReSharper reports error conflicts, which leave usages it couldn't update correctly to fix by hand. Default: false." }
                                                },
                                   required = new[] { "newName" }
                                };

   public object Execute(JObject arguments)
   {
      var newName = arguments["newName"]?.ToString();
      if(string.IsNullOrWhiteSpace(newName))
         return new { error = "'newName' is required and must be a non-empty string." };

      var dryRun = arguments["dryRun"]?.Value<bool>() ?? false;
      var applyDespiteConflicts = arguments["applyDespiteConflicts"]?.Value<bool>() ?? false;

      var (named, error) = PsiHelpers.ResolveFromArgs(
         _solution,
         arguments["symbolName"]?.ToString(),
         arguments["kind"]?.ToString(),
         arguments["filePath"]?.ToString(),
         arguments["line"]?.Value<int>() ?? 0,
         arguments["column"]?.Value<int>() ?? 0);

      if(error != null) return error;

      var oldName = named.ShortName;
      var declaredElement = TheElementARenameStartsFrom(named);
      if(declaredElement == null)
         return new { error = $"Symbol '{oldName}' cannot be renamed: ReSharper finds no element for its rename to start from.", oldName, newName };

      // Same name: nothing to do.
      if(string.Equals(oldName, newName, StringComparison.Ordinal))
      {
         return new
                {
                   applied = false,
                   oldName,
                   newName,
                   conflicts = Array.Empty<object>(),
                   note = "New name is identical to the current name; nothing to rename."
                };
      }

      // Verify the element can actually be renamed before we set anything up.
      var factory = new AtomicRenamesFactory();
      var availability = factory.CheckRenameAvailability(declaredElement);
      if(availability != RenameAvailabilityCheckResult.CanBeRenamed)
      {
         return new
                {
                   error = $"Symbol '{oldName}' cannot be renamed: {availability}.",
                   oldName,
                   newName
                };
      }

      var psiServices = _solution.GetPsiServices();

      // The plain RefactoringDriver swallows conflicts; the WithConflicts variant records them.
      var storage = new RefactoringDriverStorage();
      var driver = new RefactoringDriverWithConflicts(storage);

      // A short-lived lifetime for the workflow/data-model scope.
      var lifetimeDefinition = new LifetimeDefinition();
      try
      {
         var lifetime = lifetimeDefinition.Lifetime;

         // The rename machinery reads the ambient CompilationContextCookie (e.g. the naming
         // suggestions built inside the RenameDataModel constructor resolve runtime features
         // through it). The IDE's rename action establishes that cookie before invoking the
         // workflow; headless we must establish it ourselves, otherwise the machinery falls
         // back to the universal context and NREs deep inside GetRuntimeFeatures.
         var resolveContext = declaredElement.GetSourceFiles().FirstOrDefault()?.ResolveContext;
         if(resolveContext == null)
            return new { error = $"Symbol '{oldName}' has no source file; only source-defined symbols can be renamed.", oldName, newName };

         using var contextCookie = CompilationContextCookie.GetOrCreate(resolveContext);

         // Subclass exposes DataModel assignment (the base setter is protected) and lets us
         // drive the workflow without a UI IDataContext.
         var workflow = new HeadlessRenameWorkflow(_solution, "DotNetAnalysisAndRefactoringMcp.Rename")
                        {
                           WorkflowExecuterLifetime = lifetime
                        };

         var languageType = RenameUtil.GetPsiLanguageTypeOrKnownLanguage(declaredElement);
         var renameHelper = workflow.LanguageSpecific[languageType];

         // Build the options model. NOTE: the RenameDataModel constructor mutates this
         // model (it sets HasUI=true, RenameDerived=true, ChangeTextOccurrences from
         // settings), so we must force our non-interactive, code-only flags AFTER the
         // data model has been constructed — not before.
         var model = renameHelper.GetOptionsModel(declaredElement, null, lifetime);

         var dataModel = new RenameDataModel(
                            [declaredElement],
                            null,
                            dryRun ? RenameFilesOption.NothingToRename : RenameFilesOption.RenameWithoutConfirmation,
                            lifetime,
                            _solution,
                            model,
                            workflow)
                         {
                            NewName = newName
                         };

         // Force non-interactive behaviour (overriding the ctor's defaults).
         // IMPORTANT: QuickRename must stay false. QuickRename=true short-circuits the
         // solution-wide usage search in LoadBaseRenames (it is meant for inline/local
         // rename where usages are trivial), which would leave references un-rewritten.
         // With QuickRename=false the workflow searches the whole solution for usages.
         // HasUI=false keeps it headless (no pages/dialogs are shown).
         // RenameFile: a real rename renames a type's file named after it (RenameTheFiles); a dry
         // run none, as its rollback wouldn't undo the project model's change.
         model.HasUI = false;
         model.QuickRename = false;
         model.RenameDerived = false;
         model.RenameFile = !dryRun;
         model.ChangeTextOccurrences = false;

         workflow.AssignDataModel(dataModel);

         // The call opened no PSI transaction for us, as Rider's WorkflowExecuter opens one around a refactoring's
         // Execute. Open our own, which a real rename commits as it ends, and a dry run rolls back, so none of the
         // edits Execute() makes are ever persisted — while the conflict/affected-file analysis we capture from the
         // (now discarded) tree mutation remains valid for reporting.
         bool executed;
         var unreadable = new SkippedElements();
         List<object> conflicts;
         IReadOnlyList<IPsiSourceFile> affected;
         using(var transaction = EditTransaction.Begin(psiServices, "DotNetAnalysisAndRefactoringMcp.rename_symbol", dryRun))
         {
            // Build atomic renames AND search the solution for usages (populates the
            // references store the atomic renames consume). NameHasChanged ensures the load
            // is not short-circuited.
            dataModel.NameHasChanged = true;
            dataModel.LoadBaseRenames(NullProgressIndicator.Create(), workflow, forceReload: true);

            if(dataModel.AllRenames.Renames.Count == 0)
            {
               transaction.Rollback();
               return new { error = $"No applicable renames were produced for '{oldName}'.", oldName, newName };
            }

            // Execute the rename. RenameRefactoring.Execute performs cold conflict search
            // (recorded in the driver) and then the actual declaration/usage rewrites.
            executed = new RenameRefactoring(workflow, _solution, driver).Execute(NullProgressIndicator.Create());

            // What ReSharper fails to read costs only that conflict's value, and the answer says so.
            conflicts = ExtractConflicts(driver, unreadable);
            affected = transaction.ChangedFiles;
            if(!dryRun && !applyDespiteConflicts && HasAnErrorConflict(driver, unreadable))
            {
               transaction.Rollback();
               return new
                      {
                         applied = false,
                         refused = "ReSharper reports an error conflict, such as a usage it can't update correctly: the rename would leave it "
                                 + "broken, so nothing was changed. Fix what the conflicts name first, or pass applyDespiteConflicts: true "
                                 + "and fix them by hand after.",
                         oldName,
                         newName,
                         conflicts,
                         wouldChange = ChangedFiles(affected),
                         skipped = unreadable.Line
                      };
            }
         }

         var changedFiles = ChangedFiles(affected);
         if(dryRun)
         {
            // The transaction rolled back as it ended, discarding all edits.
            return new
                   {
                      applied = false,
                      dryRun = true,
                      oldName,
                      newName,
                      conflicts,
                      changedFiles,
                      skipped = unreadable.Line
                   };
         }

         // The auto-commit cookie committed on Dispose, persisting the rename.
         return new
                {
                   applied = executed,
                   oldName,
                   newName,
                   conflicts,
                   changedFiles,
                   renamedFiles = executed ? RenameTheFiles(workflow) : [],
                   skipped = unreadable.Line
                };
      }
      finally
      {
         lifetimeDefinition.Terminate();
      }
   }

   /// <summary>
   ///   The element that a rename of <paramref name="named"/> renames first, as Rider's Rename has it: a positional
   ///   record's property is declared by the record's parameter, so renaming the property renames the parameter, which
   ///   renames the property with it. Most elements are their own.
   /// </summary>
   /// <remarks>
   ///   <para>
   ///     Until 2026-11-15 the tool renamed the element as named, and a positional record's property, which has no
   ///     declaration of its own, failed: "has no source file".
   ///   </para>
   ///   <para>
   ///     Null when one of ReSharper's <see cref="IPrimaryDeclaredElementForRenameProvider"/>s finds none to start from.
   ///   </para>
   /// </remarks>
   static IDeclaredElement TheElementARenameStartsFrom(IDeclaredElement named) =>
      RenameRefactoringService.Instance.GetPrimevalDeclaredElement(named, null);

   /// <summary>
   ///   The rest of Rider's Rename, once its PSI transaction is committed (<see cref="RenameWorkflow.PostExecute"/>):
   ///   renames the files named after a renamed type, through the project model, and the usages Rider's frontend keeps.
   ///   The files it renamed, from and to.
   /// </summary>
   /// <remarks>
   ///   Until 2026-11-15 the tool stopped after the transaction, and a renamed type's file kept the old name. A dry run
   ///   renames no file: the project model's changes aren't the PSI transaction's, and its rollback wouldn't undo them.
   /// </remarks>
   static List<object> RenameTheFiles(RenameWorkflow workflow)
   {
      List<object> renamed = [.. workflow.IterateRenameFiles().Select(it => new { from = it.Rename.SourceLocation.FullPath, to = it.Rename.SourceLocation.Directory.Combine(it.NewName).FullPath })];
      workflow.PostExecute(NullProgressIndicator.Create());
      return renamed;
   }

   /// <summary>
   ///   Whether ReSharper reports a valid conflict of <see cref="ConflictSeverity.Error"/>, such as "Usage of
   ///   IMemoryImageStateStore.ChangesCommittedElsewhere cannot be updated correctly".
   /// </summary>
   /// <remarks>
   ///   Rider shows such conflicts before it applies a rename, and its user chooses. Until 2026-11-24 the tool applied the
   ///   rename anyway: a <c>cref</c> lost its type qualifier, and the build warned CS1574 where the conflict had said.
   /// </remarks>
   static bool HasAnErrorConflict(RefactoringDriverWithConflicts driver, SkippedElements unreadable) =>
      driver.Conflicts.Any(conflict => conflict != null && conflict.Severity == ConflictSeverity.Error &&
                                       unreadable.Read(() => "whether a conflict is valid", () => (bool?)conflict.IsValid) != false);

   static List<object> ExtractConflicts(RefactoringDriverWithConflicts driver, SkippedElements unreadable)
   {
      var result = new List<object>();
      foreach(var conflict in driver.Conflicts)
      {
         if(conflict == null) continue;
         result.Add(new
                    {
                       message = unreadable.Read(() => "a conflict's description", () => conflict.Description),
                       severity = conflict.Severity.ToString(),
                       isValid = unreadable.Read(() => "whether a conflict is valid", () => (bool?)conflict.IsValid)
                    });
      }

      return result;
   }

   /// <summary>
   ///   The files the rename changed: the ones its transaction changed, <paramref name="affected"/>, before a dry run's
   ///   is rolled back. The files of the implementations and overrides it renamed with the symbol are among them.
   /// </summary>
   /// <remarks>
   ///   Until 2026-11-15 the list was put together from the rename's data model, of the symbol's declarations and the
   ///   references to each element renamed, and it left out the files of the implementations: a rename of an
   ///   interface's method with implementations in four projects listed only the interface's file and the callers'.
   /// </remarks>
   static List<object> ChangedFiles(IEnumerable<IPsiSourceFile> affected) =>
   [
      .. affected.Select(it => it.GetLocation().FullPath)
                 .Where(it => it.Length > 0)
                 .Distinct()
                 .OrderBy(it => it, StringComparer.Ordinal)
                 .Select(it => (object)new { file = it, fileName = Path.GetFileName(it) })
   ];

   /// <summary>
   /// Headless rename workflow: the base <c>DataModel</c> setter is <c>protected</c> and the
   /// usual <c>Initialize(IDataContext)</c> path requires UI data we do not have, so we
   /// subclass to inject a pre-built data model directly.
   /// </summary>
   sealed class HeadlessRenameWorkflow : RenameWorkflow
   {
      public HeadlessRenameWorkflow(ISolution solution, string actionId)
         : base(solution, actionId) {}

      public void AssignDataModel(RenameDataModel dataModel) => DataModel = dataModel;
   }
}
