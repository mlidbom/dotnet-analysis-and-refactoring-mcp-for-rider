#nullable enable
using System.Collections.Generic;
using System.Linq;
using DotNetAnalysisAndRefactoringMcp.Tools;
using JetBrains.Application.Progress;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Refactorings;
using JetBrains.ReSharper.Feature.Services.Refactorings.Specific.Rename;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Refactorings.Rename;

namespace DotNetAnalysisAndRefactoringMcp.Renames;

/// <summary>
///   Rider's Rename, run with no dialog: ReSharper's <see cref="RenameWorkflow"/>, set up for a rename of one element to
///   a new name that searches the whole solution for its usages, and renames a type's file named after it unless it is
///   a dry run.
/// </summary>
/// <remarks>
///   <para>
///     Rider sets its workflow up through <c>Initialize(IDataContext)</c>, from the data of its UI, which a tool doesn't
///     have, and only a subclass can set the <see cref="RenameWorkflowBase.DataModel"/>: so this one builds it.
///   </para>
///   <para>
///     Rider's <c>WorkflowExecuter</c> opens a PSI transaction around a refactoring's run. Here the caller opens it, an
///     <see cref="EditTransaction"/>, which a real rename commits and a dry run rolls back.
///   </para>
/// </remarks>
sealed class HeadlessRenameWorkflow : RenameWorkflow
{
   /// <summary>Collects the conflicts ReSharper's refactoring finds, which the plain <see cref="RefactoringDriver"/> discards.</summary>
   readonly RefactoringDriverWithConflicts _driver = new(new RefactoringDriverStorage());

   HeadlessRenameWorkflow(ISolution solution, Lifetime lifetime) : base(solution, "DotNetAnalysisAndRefactoringMcp.Rename") =>
      WorkflowExecuterLifetime = lifetime;

   /// <summary>
   ///   A rename of <paramref name="element"/> as <paramref name="request"/> asks, which lasts as long as
   ///   <paramref name="lifetime"/>.
   /// </summary>
   public static HeadlessRenameWorkflow Of(ISolution solution, IDeclaredElement element, RenameRequest request, Lifetime lifetime)
   {
      var workflow = new HeadlessRenameWorkflow(solution, lifetime);
      var options = workflow.LanguageSpecific[RenameUtil.GetPsiLanguageTypeOrKnownLanguage(element)].GetOptionsModel(element, null, lifetime);
      var dataModel = new RenameDataModel(
                         [element],
                         null,
                         request.IsADryRun ? RenameFilesOption.NothingToRename : RenameFilesOption.RenameWithoutConfirmation,
                         lifetime,
                         solution,
                         options,
                         workflow)
                      {
                         NewName = request.NewName
                      };
      SetTheOptionsForNoDialog(options, request);
      workflow.DataModel = dataModel;
      return workflow;
   }

   /// <summary>
   ///   Builds the renames of the element and of what is renamed with it, and searches the solution for the usages they
   ///   rewrite: what makes the rename semantic, not textual.
   /// </summary>
   public void FindTheRenames()
   {
      DataModel.NameHasChanged = true; // or the load is skipped
      DataModel.LoadBaseRenames(NullProgressIndicator.Create(), this, forceReload: true);
   }

   /// <summary>Whether <see cref="FindTheRenames"/> found nothing to rename.</summary>
   public bool FoundNothingToRename => DataModel.AllRenames.Renames.Count == 0;

   /// <summary>
   ///   Rewrites the declarations and the usages, in the PSI transaction the caller opened, after ReSharper's search for
   ///   conflicts has collected them. Whether ReSharper's refactoring reports that it succeeded.
   /// </summary>
   public bool RenameTheCode() => new RenameRefactoring(this, Solution, _driver).Execute(NullProgressIndicator.Create());

   /// <summary>The conflicts <see cref="RenameTheCode"/> found, read now: inside its transaction, before a rollback.</summary>
   public RenameConflicts ReadTheConflicts() => new(_driver);

   /// <summary>
   ///   The rest of Rider's Rename, once its PSI transaction is committed (<see cref="RenameWorkflow.PostExecute"/>):
   ///   renames the files named after a renamed type, through the project model, and the usages Rider's frontend keeps.
   ///   The files it renamed, from and to.
   /// </summary>
   /// <remarks>
   ///   Until 2026-11-15 the tool stopped after the transaction, and a renamed type's file kept the old name. A dry run
   ///   renames no file: the project model's changes aren't the PSI transaction's, and its rollback wouldn't undo them.
   /// </remarks>
   public IReadOnlyList<RenamedFile> RenameTheFiles()
   {
      List<RenamedFile> renamed = [.. IterateRenameFiles().Select(it => new RenamedFile(it.Rename.SourceLocation, it.NewName))];
      PostExecute(NullProgressIndicator.Create());
      return renamed;
   }

   /// <summary>
   ///   Sets <paramref name="options"/> as Rider's dialog would leave them with none shown: the
   ///   <see cref="RenameDataModel"/>'s constructor sets them for its dialog, from the settings, so this comes after it.
   /// </summary>
   static void SetTheOptionsForNoDialog(CustomRenameModel options, RenameRequest request)
   {
      options.HasUI = false;
      options.QuickRename = false; // true skips the solution-wide search for usages, which is for a local's rename, and leaves them as they were
      options.RenameDerived = false;
      options.RenameFile = !request.IsADryRun; // a dry run's rollback wouldn't undo the project model's change
      options.ChangeTextOccurrences = false;
   }
}
