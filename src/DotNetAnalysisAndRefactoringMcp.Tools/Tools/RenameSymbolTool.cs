using DotNetAnalysisAndRefactoringMcp.Renames;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Refactorings.Specific.Rename;
using JetBrains.ReSharper.Psi;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   <c>rename_symbol</c>: a safe, semantic, solution-wide rename. It runs as Rider runs its Rename
///   (<see cref="IMcpMainThreadTool"/>): on the R# main thread, under a read lock, documents committed, in a PSI
///   transaction of its own, an <see cref="EditTransaction"/>, which a real rename commits and a dry run rolls back.
/// </summary>
/// <remarks>
///   ReSharper's own Rename does the work, run with no dialog: <see cref="HeadlessRenameWorkflow"/>. Its answer is the
///   <see cref="RenameOutcome"/>'s.
/// </remarks>
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

      var request = new RenameRequest(named, newName, isADryRun: dryRun, appliesDespiteConflicts: applyDespiteConflicts);

      var declaredElement = TheElementARenameStartsFrom(named);
      if(declaredElement == null)
         return new { error = $"Symbol '{request.OldName}' cannot be renamed: ReSharper finds no element for its rename to start from." };

      if(request.KeepsTheName)
         return $"{request.Symbol} is named {request.NewName} already: nothing to rename.";

      var availability = new AtomicRenamesFactory().CheckRenameAvailability(declaredElement);
      if(availability != RenameAvailabilityCheckResult.CanBeRenamed)
         return new { error = $"Symbol '{request.OldName}' cannot be renamed: {availability}." };

      if(declaredElement.GetSourceFiles().FirstOrDefault()?.ResolveContext == null)
         return new { error = $"Symbol '{request.OldName}' has no source file; only source-defined symbols can be renamed." };

      return Rename(declaredElement, request);
   }

   /// <summary>
   ///   Renames <paramref name="element"/> as <paramref name="request"/> asks, with ReSharper's Rename, and answers with
   ///   the <see cref="RenameOutcome"/>.
   /// </summary>
   object Rename(IDeclaredElement element, RenameRequest request)
   {
      using var lifetime = new LifetimeDefinition();
      using var compilationContext = CompilationContexts.For(element);
      var workflow = HeadlessRenameWorkflow.Of(_solution, element, request, lifetime.Lifetime);

      bool renamedTheCode;
      RenameOutcome outcome;
      using(var transaction = EditTransaction.Begin(_solution.GetPsiServices(), "DotNetAnalysisAndRefactoringMcp.rename_symbol", request.IsADryRun))
      {
         workflow.FindTheRenames();
         if(workflow.FoundNothingToRename)
         {
            transaction.Rollback();
            return new { error = $"No applicable renames were produced for '{request.OldName}'." };
         }

         renamedTheCode = workflow.RenameTheCode();
         outcome = new RenameOutcome(request, workflow.ReadTheConflicts(), transaction.ChangedFiles, SolutionRoot.Of(_solution));
         if(outcome.IsRefused)
         {
            transaction.Rollback();
            return outcome.Refused();
         }
      }

      // A dry run's transaction rolled back as it ended; a real rename's committed.
      if(request.IsADryRun) return outcome.Previewed();
      return renamedTheCode ? outcome.Applied(workflow.RenameTheFiles()) : outcome.NotCompleted();
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
}
