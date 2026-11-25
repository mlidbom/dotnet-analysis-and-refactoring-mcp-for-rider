#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;

namespace DotNetAnalysisAndRefactoringMcp.TypeHierarchies;

/// <summary>One type of a <see cref="TypeHierarchy"/>, one line of its outline, with the next level listed under it.</summary>
public sealed class TypeHierarchyNode
{
   public ITypeElement Type { get; }

   /// <summary>
   ///   Its qualified name as the tree writes it: with the type arguments a subtype gives it, for a supertype,
   ///   <c>System.IEquatable&lt;Callee&gt;</c>; else with its type parameters, <c>Shapes.Stack&lt;T&gt;</c>.
   /// </summary>
   public string Name { get; }

   /// <summary>The next level up or down, listed under it.</summary>
   public List<TypeHierarchyNode> TypesUnderIt { get; } = [];

   /// <summary>Whether it is listed above in the tree, the levels beyond it with it, which aren't listed again.</summary>
   public bool IsListedAbove { get; set; }

   public TypeHierarchyNode(ITypeElement type, string name)
   {
      Type = type;
      Name = name;
   }

   /// <summary>A type's name with its type parameters, <c>Shapes.Stack&lt;T&gt;</c>.</summary>
   public static string NameOf(ITypeElement type) =>
      PsiHelpers.GetQualifiedName(type) + TypeListOf(type.TypeParameters.Select(it => it.ShortName));

   /// <summary>A supertype's name as a type writes it among its supertypes, <c>System.IEquatable&lt;Callee&gt;</c>.</summary>
   public static string NameAsWritten(IDeclaredType supertype, ITypeElement element)
   {
      var substitution = supertype.GetSubstitution();
      return PsiHelpers.GetQualifiedName(element)
           + TypeListOf(element.TypeParameters.Select(it => substitution[it].GetPresentableName(CSharpLanguage.Instance)));
   }

   /// <summary>Its line, <c>abstract class Shapes.Drawing.Renaming.Segment  Shapes.Drawing\Renaming\Segment.cs:4</c>, and the levels under it.</summary>
   public void WriteTo(StringBuilder text, string indent, SolutionRoot root)
   {
      text.Append($"{indent}{Signature.KindOf(Type)} {Name}  {PsiHelpers.Location(Type, root)}");
      text.Append(IsListedAbove ? "  (listed above)\n" : "\n");

      foreach(var next in TypesUnderIt)
         next.WriteTo(text, indent + "  ", root);
   }

   static string TypeListOf(IEnumerable<string> types) =>
      types.ToList() is { Count: > 0 } list ? $"<{string.Join(", ", list)}>" : "";
}
