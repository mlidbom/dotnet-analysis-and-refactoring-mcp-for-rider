#nullable enable
using System.Collections.Generic;
using System.Linq;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Tree;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   A member and the other members of its type that share its name and kind: a method's overloads, a type's
///   constructors. A name without a parameter list, such as <c>NoteSerializer.Serialize</c>, stands for all of them.
/// </summary>
/// <remarks>
///   Looking a member up by name finds one of them, since they share their qualified name. On 2026-11-24
///   get_symbol_source with allDeclarations returned one of the four overloads of JAStudio's
///   <c>NoteSerializer.Serialize</c>, though it promises every overload.
/// </remarks>
static class Overloads
{
   /// <summary><paramref name="member"/> and its overloads, in the order they are declared: only <paramref name="member"/> when it has none.</summary>
   public static IReadOnlyList<IDeclaredElement> Of(IDeclaredElement member)
   {
      if(member is not ITypeMember typeMember || typeMember.GetContainingType() is not {} type) return [member];
      var kind = member.GetElementType();
      var overloads = type.GetMembers().Where(it => it.ShortName == member.ShortName && Equals(it.GetElementType(), kind))
                          .OrderBy(DeclaredAt).ToList<IDeclaredElement>();
      return overloads.Contains(member) ? overloads : [member];
   }

   /// <summary>Where a member's first declaration is, for the order of the source; a compiled member comes after the declared ones.</summary>
   static (bool Compiled, string File, int Offset) DeclaredAt(IDeclaredElement member) =>
      member.GetDeclarations().FirstOrDefault() is {} declaration
         ? (false, declaration.GetSourceFile()?.GetLocation().FullPath ?? "", declaration.GetTreeStartOffset().Offset)
         : (true, "", 0);
}
