#nullable enable
using System.Collections.Generic;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Psi.Util;

namespace DotNetAnalysisAndRefactoringMcp.TypeHierarchies;

/// <summary>
///   The types a type inherits from, its base class and the interfaces it implements, and theirs, to a depth; compiled
///   ones too, such as <c>System.IDisposable</c>. Not <c>object</c>, which every class inherits from.
/// </summary>
public sealed class Supertypes : TypeHierarchy
{
   Supertypes(ITypeElement start, int maxDepth) : base(start, maxDepth) {}

   public static Supertypes Of(ITypeElement type, int maxDepth)
   {
      var supertypes = new Supertypes(type, maxDepth);
      supertypes.Fill();
      return supertypes;
   }

   protected override string Heading(string levels) => $"Supertypes, {levels} up";

   protected override string HeadingOfNone => "No supertypes besides object";

   protected override IReadOnlyList<TypeHierarchyNode> NextLevelOf(ITypeElement type)
   {
      var supertypes = new List<TypeHierarchyNode>();
      foreach(var supertype in type.GetSuperTypes())
         Skipped.Guard(() => supertype.GetPresentableName(CSharpLanguage.Instance),
                       () =>
                       {
                          if(supertype.GetTypeElement() is { } element && !element.IsObjectClass())
                             supertypes.Add(new TypeHierarchyNode(element, TypeHierarchyNode.NameAsWritten(supertype, element)));
                       });
      return supertypes;
   }
}
