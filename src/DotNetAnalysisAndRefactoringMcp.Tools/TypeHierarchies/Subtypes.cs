#nullable enable
using System.Collections.Generic;
using System.Linq;
using JetBrains.Application.Progress;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Impl;
using JetBrains.ReSharper.Psi.Search;

namespace DotNetAnalysisAndRefactoringMcp.TypeHierarchies;

/// <summary>
///   The types in the solution that inherit from a type, and theirs, to a depth: its derived classes, or the types that
///   implement an interface and the interfaces that extend it. Each is listed under the type it names among its own
///   supertypes, not again under that type's supertypes.
/// </summary>
public sealed class Subtypes : TypeHierarchy
{
   readonly ISolution _solution;

   Subtypes(ITypeElement start, int maxDepth, ISolution solution) : base(start, maxDepth) => _solution = solution;

   public static Subtypes Of(ITypeElement type, int maxDepth, ISolution solution)
   {
      var subtypes = new Subtypes(type, maxDepth, solution);
      subtypes.Fill();
      return subtypes;
   }

   protected override string Heading(string levels) => $"Subtypes in the solution, {levels} down";

   protected override string HeadingOfNone => "No subtypes in the solution";

   /// <summary>
   ///   ReSharper's search finds every type that inherits from <paramref name="type"/>, through others too: only those
   ///   that name it among their own supertypes are the next level.
   /// </summary>
   protected override IReadOnlyList<TypeHierarchyNode> NextLevelOf(ITypeElement type)
   {
      var inheritors = new List<ITypeElement>();
      _solution.GetPsiServices().Finder.FindInheritors(
         type,
         SearchDomainFactory.Instance.CreateSearchDomain(_solution, false),
         new FindResultConsumer(result =>
         {
            if(result is FindResultInheritedElement { DeclaredElement: ITypeElement inheritor })
               inheritors.Add(inheritor);
            return FindExecution.Continue;
         }),
         NullProgressIndicator.Create());

      var subtypes = new List<TypeHierarchyNode>();
      foreach(var inheritor in inheritors)
         Skipped.Guard(inheritor,
                       () =>
                       {
                          if(NamesAmongItsSupertypes(inheritor, type))
                             subtypes.Add(new TypeHierarchyNode(inheritor, TypeHierarchyNode.NameOf(inheritor)));
                       });
      return subtypes;
   }

   static bool NamesAmongItsSupertypes(ITypeElement inheritor, ITypeElement type) =>
      inheritor.GetSuperTypes().Any(it => DeclaredElementEqualityComparer.TypeElementComparer.Equals(it.GetTypeElement(), type));
}
