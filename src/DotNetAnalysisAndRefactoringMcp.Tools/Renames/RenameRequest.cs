#nullable enable
using System;
using JetBrains.ReSharper.Psi;

namespace DotNetAnalysisAndRefactoringMcp.Renames;

/// <summary>
///   What a <c>rename_symbol</c> call asks for: a rename of a symbol to a new name, applied or only previewed, and
///   whether despite error conflicts.
/// </summary>
/// <remarks>
///   The symbol's names are read as the call starts: the rename may leave the element renamed, or no longer valid,
///   before the answer names it.
/// </remarks>
sealed class RenameRequest
{
   /// <summary>A rename of <paramref name="named"/>, the symbol as the call names it, to <paramref name="newName"/>.</summary>
   public RenameRequest(IDeclaredElement named, string newName, bool isADryRun, bool appliesDespiteConflicts)
   {
      OldName = named.ShortName;
      Symbol = $"{Signature.KindOf(named)} {PsiHelpers.GetQualifiedName(named)}";
      NewName = newName;
      IsADryRun = isADryRun;
      AppliesDespiteConflicts = appliesDespiteConflicts;
   }

   /// <summary>The symbol's name before the rename: <c>Area</c>.</summary>
   public string OldName { get; }

   /// <summary>The symbol by its kind and qualified name, as the answers name it: <c>method Shapes.IShape.Area</c>.</summary>
   public string Symbol { get; }

   public string NewName { get; }

   /// <summary>
   ///   Whether the call only previews the rename, <c>dryRun</c>: what it changes is rolled back, and it renames no file.
   /// </summary>
   public bool IsADryRun { get; }

   /// <summary>
   ///   Whether the rename is applied even when ReSharper reports an error conflict, <c>applyDespiteConflicts</c>, which
   ///   leaves the usages it couldn't update correctly to fix by hand.
   /// </summary>
   public bool AppliesDespiteConflicts { get; }

   /// <summary>Whether <see cref="NewName"/> is the name the symbol has already.</summary>
   public bool KeepsTheName => string.Equals(OldName, NewName, StringComparison.Ordinal);

   /// <summary>
   ///   <see cref="Symbol"/> to <see cref="NewName"/>, as the answers' first lines say it:
   ///   <c>method Shapes.IShape.Area to Surface</c>.
   /// </summary>
   public override string ToString() => $"{Symbol} to {NewName}";
}
