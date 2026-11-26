#nullable enable
using System.Collections.Generic;
using System.Linq;
using JetBrains.ReSharper.Feature.Services.Refactorings;
using JetBrains.ReSharper.Feature.Services.Refactorings.Conflicts;

namespace DotNetAnalysisAndRefactoringMcp.Renames;

/// <summary>
///   The conflicts ReSharper reported for a rename, such as a usage it can't update correctly: each on a line of its
///   own for the answer, and whether one is an error, which refuses the rename.
/// </summary>
/// <remarks>
///   Read from the <see cref="RefactoringDriverWithConflicts"/> that collected them, inside the rename's transaction,
///   before a rollback.
/// </remarks>
sealed class RenameConflicts
{
   /// <summary>The conflicts' and their parts' failures to be read, which cost only what failed.</summary>
   readonly SkippedElements _unreadable = new();

   public RenameConflicts(RefactoringDriverWithConflicts driver)
   {
      var conflicts = driver.Conflicts.Where(it => it != null).ToList();
      Lines = [.. conflicts.Select(LineOf)];
      //bug: an error conflict whose validity ReSharper fails to read is listed twice in UnreadableLine, as LineOf has
      //   read IsValid too. Read each conflict once, into a value both its line and HasAnError are made from.
      HasAnError = conflicts.Any(IsAValidError);
   }

   /// <summary>Each conflict on a line of its own: <c>error conflict: Containing type has the same name as the new name</c>.</summary>
   public IReadOnlyList<string> Lines { get; }

   /// <summary>
   ///   Whether ReSharper reports a valid conflict of <see cref="ConflictSeverity.Error"/>, such as "Usage of
   ///   IMemoryImageStateStore.ChangesCommittedElsewhere cannot be updated correctly".
   /// </summary>
   /// <remarks>
   ///   Rider shows such conflicts before it applies a rename, and its user chooses. Until 2026-11-24 the tool applied the
   ///   rename anyway: a <c>cref</c> lost its type qualifier, and the build warned CS1574 where the conflict had said.
   /// </remarks>
   public bool HasAnError { get; }

   /// <summary>The line saying what of the conflicts ReSharper failed to read, or <c>null</c> when it read them all.</summary>
   public string? UnreadableLine => _unreadable.Line;

   string LineOf(IConflict conflict)
   {
      var description = _unreadable.Read(() => "a conflict's description", () => conflict.Description) ?? "[its description couldn't be read]";
      var noLongerValid = _unreadable.Read(() => "whether a conflict is valid", () => (bool?)conflict.IsValid) == false ? " [no longer valid]" : "";
      return $"{conflict.Severity.ToString().ToLowerInvariant()} conflict: {description}{noLongerValid}";
   }

   bool IsAValidError(IConflict conflict) =>
      conflict.Severity == ConflictSeverity.Error && _unreadable.Read(() => "whether a conflict is valid", () => (bool?)conflict.IsValid) != false;
}
