#nullable enable
using System.Collections.Generic;
using System.Linq;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Tree;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   The declaration of a symbol that a tool shows when it shows one: for a type declared in parts, the part in the file
///   named after it, such as <c>IThreadGate.cs</c> rather than <c>IThreadGate.Implementation.cs</c>.
/// </summary>
/// <remarks>
///   ReSharper lists a symbol's declarations in no order a reader would choose: the part in <c>Palette.Mixing.cs</c>
///   before the one in <c>Palette.cs</c>. The symbol linker's session saw it list the part edited last first: a freshly
///   indexed solution gave <c>Palette.cs</c>, and after an edit of <c>Palette.Mixing.cs</c> that part. On 2026-11-24 get_symbol_source for Compze's <c>IThreadGate</c> returned the
///   220 lines of its private implementation's part, and the agent read <c>IThreadGate.cs</c> after it.
/// </remarks>
static class MainDeclaration
{
   /// <summary>The declaration to show, or nothing for a symbol without one, such as a compiled one.</summary>
   public static IDeclaration? Of(IDeclaredElement element) => InOrder(element).FirstOrDefault();

   /// <summary><paramref name="element"/>'s declarations, the main one first and the rest in ReSharper's order.</summary>
   public static IList<IDeclaration> InOrder(IDeclaredElement element)
   {
      var declarations = element.GetDeclarations();
      var main = declarations.FirstOrDefault(it => IsInTheFileNamedAfter(element, it));
      return main == null ? declarations : [main, ..declarations.Where(it => it != main)];
   }

   static bool IsInTheFileNamedAfter(IDeclaredElement element, IDeclaration declaration) =>
      declaration.GetSourceFile()?.GetLocation().NameWithoutExtension == element.ShortName;
}
