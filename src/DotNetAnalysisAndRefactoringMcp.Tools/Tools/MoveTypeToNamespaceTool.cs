#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Application.DataContext;
using JetBrains.Application.Progress;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.DataContext;
using JetBrains.ReSharper.Feature.Services.Refactorings;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.CSharp.Impl;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.CSharp.Util;
using JetBrains.ReSharper.Psi.DataContext;
using JetBrains.ReSharper.Psi.Files;
using JetBrains.ReSharper.Psi.Search;
using JetBrains.ReSharper.Psi.Transactions;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Refactorings.Move.MoveToNamespace;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   move_type_to_namespace: ReSharper's Move to Another Namespace, headless, and what the rider MCP's tool of the same
///   name leaves undone: it checks that the declaration itself moved, and gives every file that calls the type's
///   extension methods the <c>using</c> it then needs.
/// </summary>
/// <remarks>
///   <para>
///     Driven as <see cref="RenameSymbolTool"/> drives rename: the workflow with a data provider in place of its page,
///     initialized from a data context of only the type and the solution, and the refactoring executed in a PSI
///     transaction of its own, temporary for a dry run.
///   </para>
///   <para>
///     The file isn't moved: Rider's documents hold the unsaved result, and a file moved under them would lose it. The
///     answer says so.
///   </para>
/// </remarks>
public sealed class MoveTypeToNamespaceTool : IMcpSelfTransactingWriteTool, IMcpEditTool
{
   const string ActionId = "DotNetAnalysisAndRefactoringMcp.MoveToNamespace";

   readonly ISolution _solution;

   public MoveTypeToNamespaceTool(ISolution solution) => _solution = solution;

   public string Name => "move_type_to_namespace";

   public string Description =>
      "Move a type to another namespace, as ReSharper's Move to Another Namespace does: its declaration, and every " +
      "reference and using across the solution. Then checks that the declaration itself moved, and adds the using to " +
      "every file that calls the type's extension methods. The file stays where it is: git mv it to the namespace's folder. " +
      "The usings it leaves redundant: apply_suggestions with inspectionIds 'RedundantUsingDirective' and scope 'solution'. " +
      "Name the type by symbolName or position, and the namespace by newNamespace; dryRun previews the files.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   symbolName = new { type = "string", description = "The type to move, e.g. 'MyClass' or 'Old.Namespace.MyClass'." },
                                                   filePath = new { type = "string", description = "File of the type's declaration, with line and column, instead of symbolName." },
                                                   line = new { type = "integer", description = "1-based line of the type's name." },
                                                   column = new { type = "integer", description = "1-based column of the type's name." },
                                                   newNamespace = new { type = "string", description = "The namespace to move it to (required)." },
                                                   dryRun = new { type = "boolean", description = "Report the files it would change, and change nothing. Default: false." }
                                                },
                                   required = new[] { "newNamespace" }
                                };

   public object Execute(JObject arguments)
   {
      var newNamespace = arguments["newNamespace"]?.ToString().Trim();
      if(string.IsNullOrEmpty(newNamespace))
         return new { error = "'newNamespace' is required." };
      var dryRun = arguments["dryRun"]?.Value<bool>() ?? false;

      var (element, error) = PsiHelpers.ResolveFromArgs(_solution,
                                                        arguments["symbolName"]?.ToString(),
                                                        "type",
                                                        arguments["filePath"]?.ToString(),
                                                        arguments["line"]?.Value<int>() ?? 0,
                                                        arguments["column"]?.Value<int>() ?? 0);
      if(error != null) return error;
      if(element is not ITypeElement type)
         return new { error = $"'{element.ShortName}' is a {element.GetElementType().PresentableName}, not a type." };

      var oldNamespace = type.GetContainingNamespace().QualifiedName;
      if(oldNamespace == newNamespace)
         return new { applied = false, note = $"{type.ShortName} is in {newNamespace} already." };

      using var context = CompilationContexts.For(type);
      using var lifetime = Lifetime.Define(Lifetime.Eternal, ActionId);

      var workflow = new MoveToNamespaceWorkflow(_solution, ActionId) { WorkflowExecuterLifetime = lifetime.Lifetime };
      workflow.SetDataProvider(new TargetNamespace(newNamespace!));
      var rules = DataRules.AddRule(ActionId, PsiDataConstants.DECLARED_ELEMENTS, [type])
                           .AddRule(ActionId, ProjectModelDataConstants.SOLUTION, _solution);
      if(!workflow.Initialize(_solution.GetComponent<DataContexts>().CreateWithoutDataRules(lifetime.Lifetime, rules)))
         return new { error = $"{type.ShortName} can't be moved to another namespace: a nested type, a type parameter, or a declaration with errors." };
      var invalid = workflow.GetErrorMessageText(newNamespace!).Text;
      if(!string.IsNullOrEmpty(invalid))
         return new { error = invalid };

      var shortName = type.ShortName;
      var module = type.Module;
      var extensionCallers = ExtensionMethodCallers(type);
      var affected = FilesReferencing(type).Concat(extensionCallers).Concat(SourceFilesOf(type))
                                           .Select(it => it.GetLocation().FullPath).Distinct().OrderBy(it => it, StringComparer.Ordinal).ToList();

      var driver = new RefactoringDriverWithConflicts(new RefactoringDriverStorage());
      var psiServices = _solution.GetPsiServices();
      using var transaction = dryRun
                                 ? PsiTransactionCookie.CreateTemporaryChangeCookie(psiServices, ActionId)
                                 : PsiTransactionCookie.CreateAutoCommitCookieWithCachesUpdate(psiServices, ActionId);

      var executed = workflow.CreateRefactoring(driver).Execute(NullProgressIndicator.Create());
      psiServices.Caches.Update();
      var moved = psiServices.Symbols.GetSymbolScope(module, true, true).GetTypeElementByCLRName(newNamespace + "." + shortName);
      if(!executed || moved == null)
      {
         transaction.Rollback();
         return new
                {
                   error = $"The refactoring left {shortName}'s declaration in {oldNamespace}, so nothing was changed. " +
                           "This is the half-done move the rider MCP's tool sometimes reports as applied."
                };
      }

      var usingsAdded = extensionCallers.Where(it => AddUsing(it, newNamespace!)).Select(it => it.GetLocation().FullPath).ToList();
      var madeFileScoped = SourceFilesOf(moved).Where(MakeFileScopedAsTheStyleHasIt).Select(it => it.GetLocation().FullPath).ToList();
      var root = SolutionRoot.Of(_solution);
      var answer = new StringBuilder();
      answer.AppendLine(dryRun
                           ? $"Would move {shortName} from {oldNamespace} to {newNamespace} (dry run: nothing changed). Files it may change:"
                           : $"Moved {shortName} from {oldNamespace} to {newNamespace}. Files it may have changed:");
      foreach(var file in affected) answer.AppendLine("  " + root.Relative(file));
      if(usingsAdded.Count > 0)
      {
         answer.AppendLine($"{(dryRun ? "Would add" : "Added")} using {newNamespace}; for the extension methods' callers in:");
         foreach(var file in usingsAdded) answer.AppendLine("  " + root.Relative(file));
      }
      if(madeFileScoped.Count > 0)
      {
         answer.AppendLine($"{(dryRun ? "Would make" : "Made")} the namespace file-scoped again, as the solution's code style has it, in:");
         foreach(var file in madeFileScoped) answer.AppendLine("  " + root.Relative(file));
      }

      foreach(var conflict in driver.Conflicts)
         answer.AppendLine("conflict: " + conflict.Description);
      answer.Append("The file wasn't moved: git mv it to the folder of its new namespace.");
      return answer.ToString();
   }

   /// <summary>The C# files that call the type's extension methods, which the move gives no using.</summary>
   List<IPsiSourceFile> ExtensionMethodCallers(ITypeElement type)
   {
      var files = new HashSet<IPsiSourceFile>();
      var finder = _solution.GetPsiServices().Finder;
      var domain = SearchDomainFactory.Instance.CreateSearchDomain(_solution, false);
      foreach(var method in type.Methods.Where(it => it.IsExtensionMethod))
         finder.FindReferences(method,
                               domain,
                               new FindResultConsumer(result =>
                               {
                                  if(result is FindResultReference reference && reference.Reference.GetTreeNode().GetSourceFile() is {} file
                                                                             && file.PrimaryPsiLanguage.Is<CSharpLanguage>())
                                     files.Add(file);
                                  return FindExecution.Continue;
                               }),
                               NullProgressIndicator.Create());
      files.ExceptWith(SourceFilesOf(type));
      return [.. files];
   }

   /// <summary>
   ///   Makes the namespace of <paramref name="sourceFile"/> file-scoped, when the solution's code style prefers that and
   ///   the file can have it: one namespace, block-scoped, and nothing outside it. Whether it did.
   /// </summary>
   /// <remarks>
   ///   ReSharper's move puts the types of a file in block-scoped namespaces when they are in two, and leaves the blocks
   ///   once they're in one again, after the file's other types have moved there too. Until 2026-11-15 the tool left them
   ///   too: JAStudio's <c>IBackendDataLoader.cs</c>, whose interface and class moved one after the other. ReSharper's
   ///   own fix for its <c>ArrangeNamespaceBody</c> inspection converts it so.
   /// </remarks>
   static bool MakeFileScopedAsTheStyleHasIt(IPsiSourceFile sourceFile)
   {
      var declaration = (sourceFile.GetDominantPsiFile<CSharpLanguage>() as ICSharpFile)?.NamespaceDeclarationNodesEnumerable.SingleItem;
      if(declaration == null || !TheStyleHasItFileScoped(declaration)) return false;
      FileScopedNamespaces.ConvertToFileScoped(declaration);
      return true;

      static bool TheStyleHasItFileScoped(ICSharpNamespaceDeclaration declaration) =>
         !declaration.IsFileScoped && CSharpNamespaceUtil.CanChangeStyle(declaration) && CSharpNamespaceUtil.IsFileScopedNamespaceBodyStylePreferred(declaration);
   }

   static List<IPsiSourceFile> SourceFilesOf(IDeclaredElement element) => [.. element.GetSourceFiles()];

   List<IPsiSourceFile> FilesReferencing(ITypeElement type)
   {
      var files = new HashSet<IPsiSourceFile>();
      _solution.GetPsiServices().Finder.FindReferences(type,
                                                       SearchDomainFactory.Instance.CreateSearchDomain(_solution, false),
                                                       new FindResultConsumer(result =>
                                                       {
                                                          if(result is FindResultReference reference && reference.Reference.GetTreeNode().GetSourceFile() is {} file)
                                                             files.Add(file);
                                                          return FindExecution.Continue;
                                                       }),
                                                       NullProgressIndicator.Create());
      return [.. files];
   }

   /// <summary>
   ///   Adds <c>using <paramref name="ns"/>;</c> unless the file has it, or is in that namespace or one inside it, where
   ///   the namespace's members are in scope already. Whether it added it.
   /// </summary>
   static bool AddUsing(IPsiSourceFile sourceFile, string ns)
   {
      if(sourceFile.GetDominantPsiFile<CSharpLanguage>() is not ICSharpFile file) return false;
      foreach(var declaration in file.Descendants<ICSharpNamespaceDeclaration>())
         if(declaration.QualifiedName == ns || declaration.QualifiedName.StartsWith(ns + ".", StringComparison.Ordinal))
            return false;
      foreach(var directive in file.Descendants<IUsingSymbolDirective>())
         if(directive.ImportedSymbolName?.QualifiedName == ns)
            return false;

      UsingUtil.AddImportTo(file, CSharpElementFactory.GetInstance(file).CreateUsingDirective(ns));
      return true;
   }

   sealed class TargetNamespace(string ns) : IMoveToNamespaceDataProvider
   {
      public string NewNamespace { get; } = ns;
   }
}
