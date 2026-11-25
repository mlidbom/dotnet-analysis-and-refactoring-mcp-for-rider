#nullable enable
using System.Collections.Generic;
using JetBrains.ReSharper.Psi;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   One of what <c>find_implementations</c> lists: a type that inherits the type asked about, or a member that implements
///   or overrides the member asked about, with how: directly, through other types it inherits, or indirectly.
/// </summary>
sealed class Implementation
{
   readonly IReadOnlyList<string> _typesItInheritsThrough;

   Implementation(IDeclaredElement element, bool isDirect, IReadOnlyList<string> typesItInheritsThrough)
   {
      Element = element;
      IsDirect = isDirect;
      _typesItInheritsThrough = typesItInheritsThrough;
   }

   /// <summary>The inheriting type, or the implementing or overriding member.</summary>
   public IDeclaredElement Element { get; }

   /// <summary>Whether it, or the type it is declared in, names what was asked about among its own supertypes.</summary>
   public bool IsDirect { get; }

   /// <summary>Its qualified name, as the call trees and the type hierarchy name it.</summary>
   public string Name => PsiHelpers.GetQualifiedName(Element);

   /// <summary>One that names what was asked about itself.</summary>
   public static Implementation Direct(IDeclaredElement element) => new(element, isDirect: true, []);

   /// <summary>
   ///   One that inherits what was asked about through other types: those of them it names itself, by their short names,
   ///   when they are known.
   /// </summary>
   public static Implementation Through(IDeclaredElement element, IReadOnlyList<string> typesItInheritsThrough) =>
      new(element, isDirect: false, typesItInheritsThrough);

   /// <summary>
   ///   Its line, <c>[direct] sealed class Shapes.Circle — Shapes\Circle.cs:4</c>, and under it the summary of the doc
   ///   comment written on it, if any.
   /// </summary>
   public string Text(SolutionRoot root)
   {
      var line = $"{How} {Signature.KindOf(Element)} {Name} — {PsiHelpers.Location(Element, root)}";
      return DocComment.WrittenOn(Element).SummaryLine is {} summary ? $"{line}\n  {summary}" : line;
   }

   string How =>
      IsDirect                           ? "[direct]"
      : _typesItInheritsThrough.Count > 0 ? $"[via {string.Join(", ", _typesItInheritsThrough)}]"
                                          : "[indirect]";
}
