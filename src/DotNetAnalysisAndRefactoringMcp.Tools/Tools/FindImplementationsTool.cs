using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Application.Progress;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Search;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

public class FindImplementationsTool : IMcpTool
{
   readonly ISolution _solution;

   public FindImplementationsTool(ISolution solution) => _solution = solution;

   public string Name => "find_implementations";
   public ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

   public string Description =>
      "Find all implementations of an interface, abstract class, or virtual/abstract member. " +
      "Returns the locations of all concrete implementations in the solution, " +
      "distinguishing direct implementations from indirect ones (via intermediate interfaces). " +
      "Provide either a symbolName or a file path with position: a member's name covers every overload of it; a " +
      "position, the one there. " +
      "Pass multiple symbols via the 'symbols' array to search for several in one call.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   symbolName = new { type = "string", description = "Symbol name to find implementations of (e.g. 'IMyInterface', 'Namespace.IMyInterface'). Alternative to filePath+line+column." },
                                                   kind = new { type = "string", description = "Filter by symbol kind when using symbolName: 'type', 'method', 'property', 'field', 'event'. Helps disambiguate when multiple symbols share a name." },
                                                   filePath = new { type = "string", description = "Absolute path to the file containing the symbol" },
                                                   line = new { type = "integer", description = "1-based line number of the symbol" },
                                                   column = new { type = "integer", description = "1-based column number of the symbol" },
                                                   maxResults = new { type = "integer", description = "Maximum number of implementations to return per symbol. Default: 50. Use to prevent overflow on widely-implemented interfaces." },
                                                   symbols = new
                                                             {
                                                                type = "array",
                                                                description = "Array of symbols to find implementations for in batch. Each item is an object with symbolName/kind or filePath/line/column. Results are concatenated with separators.",
                                                                items = new
                                                                        {
                                                                           type = "object",
                                                                           properties = new
                                                                                        {
                                                                                           symbolName = new { type = "string", description = "Symbol name" },
                                                                                           kind = new { type = "string", description = "Symbol kind filter" },
                                                                                           filePath = new { type = "string", description = "File path" },
                                                                                           line = new { type = "integer", description = "1-based line" },
                                                                                           column = new { type = "integer", description = "1-based column" }
                                                                                        }
                                                                        }
                                                             }
                                                },
                                   required = Array.Empty<string>()
                                };

   public object Execute(JObject arguments) =>
      ToolBatch.Of(arguments, "symbols", item: "symbolName")?.Run(ExecuteSingle) ?? ExecuteSingle(arguments);

   object ExecuteSingle(JObject arguments)
   {
      var (declaredElement, error) = PsiHelpers.ResolveFromArgs(
         _solution,
         arguments["symbolName"]?.ToString(),
         arguments["kind"]?.ToString(),
         arguments["filePath"]?.ToString(),
         arguments["line"]?.Value<int>() ?? 0,
         arguments["column"]?.Value<int>() ?? 0);

      if(error != null) return error;
      using var context = CompilationContexts.For(declaredElement);

      var maxResults = arguments["maxResults"]?.Value<int>() ?? 50;
      if(maxResults <= 0) maxResults = 50;
      if(maxResults > 1000) maxResults = 1000;

      var members = Overloads.NamedBy(arguments, declaredElement);
      var implementations = members.Select(it => ImplementationsOf(it, maxResults));
      return string.Join("\n\n", members.Count == 1 ? implementations : implementations.Prepend(Overloads.Heading(members)));
   }

   /// <summary>
   ///   The types that inherit <paramref name="declaredElement"/>, or the members that implement or override it, up to
   ///   <paramref name="maxResults"/>: those that name it directly first.
   /// </summary>
   string ImplementationsOf(IDeclaredElement declaredElement, int maxResults)
   {
      var skipped = new SkippedElements();
      var implementations = new List<Implementation>();
      var hitLimit = false;

      // For type elements (interfaces, abstract classes) — find inheritors with depth info
      if(declaredElement is ITypeElement typeElement)
      {
         var searchDomain = SearchDomainFactory.Instance.CreateSearchDomain(_solution, false);
         var psiServices = _solution.GetPsiServices();

         // Collect inheritors (with limit to prevent overflow)
         var inheritors = new List<ITypeElement>();
         psiServices.Finder.FindInheritors(
            typeElement,
            searchDomain,
            new FindResultConsumer(findResult =>
            {
               if(inheritors.Count >= maxResults)
               {
                  hitLimit = true;
                  return FindExecution.Stop;
               }

               if(findResult is FindResultInheritedElement inherited &&
                  inherited.DeclaredElement is ITypeElement inheritedType)
                  inheritors.Add(inheritedType);
               return FindExecution.Continue;
            }),
            NullProgressIndicator.Create());

         // Build a set of all inheritor FQNs for "via" resolution
         var inheritorSet = new HashSet<string>(
            inheritors.Select(i => i.GetClrName().FullName));

         foreach(var inheritor in inheritors)
         {
            skipped.Guard(inheritor,
                          () =>
                          {
                             if(!HasSource(inheritor)) return;

                             implementations.Add(IsDirectImplementor(inheritor, typeElement)
                                                    ? Implementation.Direct(inheritor)
                                                    : Implementation.Through(inheritor, FindIntermediateTypes(inheritor, typeElement, inheritorSet)));
                          });
         }
      }

      // For overridable members (virtual/abstract methods, properties) — find overrides
      if(!hitLimit && declaredElement is IOverridableMember overridable)
      {
         var psiServices = _solution.GetPsiServices();

         //refactor: the same search as Callee.Overriders and FlowTool.FindSingleImplementation. One type, such as
         //   Overriders.Of(member) beside SuperMembers, should search, with a cap and a stop, for all three.
         psiServices.Finder.FindImplementingMembers(
            overridable,
            overridable.GetSearchDomain(),
            new FindResultConsumer(findResult =>
            {
               if(implementations.Count >= maxResults)
               {
                  hitLimit = true;
                  return FindExecution.Stop;
               }

               if(findResult is FindResultOverridableMember overrideResult)
               {
                  var member = overrideResult.OverridableMember;
                  if(member == null) return FindExecution.Continue;

                  skipped.Guard(member,
                                () =>
                                {
                                   if(!HasSource(member)) return;

                                   implementations.Add(IsDeclaredInATypeThatNamesTheBaseMembersType(member)
                                                          ? Implementation.Direct(member)
                                                          : Implementation.Through(member, []));
                                });
               }

               return FindExecution.Continue;
            }),
            true,
            NullProgressIndicator.Create());
      }

      var root = SolutionRoot.Of(_solution);
      var count = implementations.Count == 1 ? "1 implementation" : $"{implementations.Count} implementations";
      var lines = new List<string> { $"{Signature.Of(declaredElement)} — {count}{(hitLimit ? ", and more past maxResults" : "")}" };
      if(implementations.Count > 0)
         lines.Add("");
      lines.AddRange(implementations.OrderBy(it => it.IsDirect ? 0 : 1).ThenBy(it => it.Name).Select(it => it.Text(root)));
      if(skipped.Line != null)
         lines.AddRange(["", skipped.Line]);

      return string.Join("\n", lines);

      bool IsDeclaredInATypeThatNamesTheBaseMembersType(IDeclaredElement member) =>
         member is IClrDeclaredElement implementing && implementing.GetContainingType() is {} containingType
      && declaredElement is IClrDeclaredElement implemented && implemented.GetContainingType() is {} implementedMembersType
      && IsDirectImplementor(containingType, implementedMembersType);
   }

   /// <summary>Whether <paramref name="element"/> is declared in the solution's source: one compiled into a library isn't listed.</summary>
   static bool HasSource(IDeclaredElement element) => MainDeclaration.Of(element) != null;

   /// <summary>
   /// Checks if inheritor directly lists the target type in its super types (not via intermediate).
   /// </summary>
   static bool IsDirectImplementor(ITypeElement inheritor, ITypeElement targetType)
   {
      var targetFqn = targetType.GetClrName().FullName;
      foreach(var superType in inheritor.GetSuperTypes())
      {
         var resolved = superType.GetTypeElement();
         if(resolved != null && resolved.GetClrName().FullName == targetFqn)
            return true;
      }

      return false;
   }

   /// <summary>
   /// Finds intermediate types through which inheritor implements targetType.
   /// Returns the short names of those intermediate types.
   /// </summary>
   static List<string> FindIntermediateTypes(
      ITypeElement inheritor,
      ITypeElement targetType,
      HashSet<string> allInheritorFqns)
   {
      var targetFqn = targetType.GetClrName().FullName;
      var via = new List<string>();

      foreach(var superType in inheritor.GetSuperTypes())
      {
         var resolved = superType.GetTypeElement();
         if(resolved == null) continue;

         var resolvedFqn = resolved.GetClrName().FullName;
         if(resolvedFqn == targetFqn) continue;

         if(allInheritorFqns.Contains(resolvedFqn) || ImplementsType(resolved, targetFqn))
            via.Add(resolved.ShortName);
      }

      return via;
   }

   /// <summary>
   /// Recursively checks if a type implements/extends the target (by FQN).
   /// </summary>
   static bool ImplementsType(ITypeElement type, string targetFqn)
   {
      foreach(var superType in type.GetSuperTypes())
      {
         var resolved = superType.GetTypeElement();
         if(resolved == null) continue;
         if(resolved.GetClrName().FullName == targetFqn) return true;
         if(ImplementsType(resolved, targetFqn)) return true;
      }

      return false;
   }
}
