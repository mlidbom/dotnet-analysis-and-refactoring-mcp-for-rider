#nullable enable
using System.Collections.Generic;
using System.Linq;
using JetBrains.ReSharper.Psi;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   One of what <c>find_implementations</c> lists: a type that inherits the type asked about, or a member that implements
///   or overrides the member asked about, with how: directly, through other types it inherits, or indirectly.
/// </summary>
sealed class Implementation
{
   /// <summary>The inheriting type, or the implementing or overriding member.</summary>
   readonly IDeclaredElement _element;

   readonly IReadOnlyList<ITypeElement> _typesItInheritsThrough;

   Implementation(IDeclaredElement element, bool isDirect, IReadOnlyList<ITypeElement> typesItInheritsThrough)
   {
      _element = element;
      IsDirect = isDirect;
      _typesItInheritsThrough = typesItInheritsThrough;
   }

   /// <summary>Whether it, or the type it is declared in, names what was asked about among its own supertypes.</summary>
   public bool IsDirect { get; }

   /// <summary>Its qualified name, as the call trees and the type hierarchy name it.</summary>
   public string Name => PsiHelpers.GetQualifiedName(_element);

   /// <summary>One that names what was asked about itself.</summary>
   public static Implementation Direct(IDeclaredElement element) => new(element, isDirect: true, []);

   /// <summary>
   ///   One that inherits what was asked about through other types: those of them it names itself, when they are known.
   /// </summary>
   public static Implementation Through(IDeclaredElement element, IReadOnlyList<ITypeElement> typesItInheritsThrough) =>
      new(element, isDirect: false, typesItInheritsThrough);

   /// <summary>One that inherits what was asked about through other types, which aren't known.</summary>
   public static Implementation Indirect(IDeclaredElement element) => new(element, isDirect: false, []);

   /// <summary>
   ///   Its line, <c>[direct] sealed class Shapes.Circle — Shapes\Circle.cs:4</c>, and under it the summary of the doc
   ///   comment written on it, if any.
   /// </summary>
   public string Text(SolutionRoot root)
   {
      var line = $"{How} {Signature.KindOf(_element)} {Name} — {PsiHelpers.Location(_element, root)}";
      return DocComment.WrittenOn(_element).SummaryLine is {} summary ? $"{line}\n  {summary}" : line;
   }

   string How =>
      IsDirect                            ? "[direct]"
      : _typesItInheritsThrough.Count > 0 ? $"[via {string.Join(", ", _typesItInheritsThrough.Select(it => it.ShortName))}]"
                                          : "[indirect]";
}
