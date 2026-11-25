#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.ReSharper.Feature.Services.Refactorings;
using JetBrains.ReSharper.Feature.Services.Refactorings.Conflicts;
using JetBrains.ReSharper.Psi;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   What a rename reported and changed, and <c>rename_symbol</c>'s answer from it: whether it was renamed, previewed or
///   refused, the conflicts ReSharper reported, each on a line, and the files whose text it changed, by their paths in
///   the work tree.
/// </summary>
/// <remarks>
///   Until 2026-11-25 the answer was JSON, each changed file's full path in it and then its name again.
/// </remarks>
sealed class RenameOutcome
{
   /// <summary>What was renamed, and to what: <c>method Shapes.IShape.Area to Surface</c>.</summary>
   readonly string _rename;

   readonly SolutionRoot _root;

   /// <summary>Each conflict ReSharper reported, on a line of its own, read inside the rename's transaction.</summary>
   readonly IReadOnlyList<string> _conflictLines;

   /// <summary>
   ///   The full paths of the files whose text the rename changed, before any of them was renamed: its transaction's,
   ///   <see cref="EditTransaction.ChangedFiles"/>. The files of the implementations and overrides it renamed with the
   ///   symbol are among them.
   /// </summary>
   /// <remarks>
   ///   Until 2026-11-15 the list was put together from the rename's data model, of the symbol's declarations and the
   ///   references to each element renamed, and it left out the files of the implementations: a rename of an
   ///   interface's method with implementations in four projects listed only the interface's file and the callers'.
   /// </remarks>
   readonly IReadOnlyList<string> _changedFiles;

   /// <summary>The conflicts' and their parts' failures to be read, which cost only what failed.</summary>
   readonly SkippedElements _unreadable = new();

   /// <summary>
   ///   The outcome of <paramref name="rename"/>: the conflicts <paramref name="driver"/> collected, and
   ///   <paramref name="changedFiles"/>. Read inside the rename's transaction, before a rollback.
   /// </summary>
   /// <param name="rename">
   ///   What is renamed, and to what, as <see cref="Describe"/> wrote it before the rename ran, which may leave the
   ///   element renamed or no longer valid.
   /// </param>
   public RenameOutcome(string rename, RefactoringDriverWithConflicts driver, IEnumerable<IPsiSourceFile> changedFiles, SolutionRoot root)
   {
      _rename = rename;
      _root = root;
      _changedFiles = [.. changedFiles.Select(it => it.GetLocation().FullPath).Where(it => it.Length > 0).Distinct()];
      var conflicts = driver.Conflicts.Where(it => it != null).ToList();
      _conflictLines = [.. conflicts.Select(LineOf)];
      HasAnErrorConflict = conflicts.Any(IsAValidError);
   }

   /// <summary>
   ///   Whether ReSharper reports a valid conflict of <see cref="ConflictSeverity.Error"/>, such as "Usage of
   ///   IMemoryImageStateStore.ChangesCommittedElsewhere cannot be updated correctly".
   /// </summary>
   /// <remarks>
   ///   Rider shows such conflicts before it applies a rename, and its user chooses. Until 2026-11-24 the tool applied the
   ///   rename anyway: a <c>cref</c> lost its type qualifier, and the build warned CS1574 where the conflict had said.
   /// </remarks>
   public bool HasAnErrorConflict { get; }

   /// <summary>What a rename of <paramref name="named"/> to <paramref name="newName"/> renames: <c>method Shapes.IShape.Area to Surface</c>.</summary>
   public static string Describe(IDeclaredElement named, string newName) => $"{Signature.KindOf(named)} {PsiHelpers.GetQualifiedName(named)} to {newName}";

   /// <summary>The answer for a rename that was applied, and renamed <paramref name="renamedFiles"/>.</summary>
   public string Applied(IReadOnlyList<RenamedFile> renamedFiles) => Text($"Renamed {_rename}.", "Changed", renamedFiles);

   /// <summary>The answer for a rename ReSharper's refactoring reported failing: what it changed nonetheless.</summary>
   public string NotCompleted() => Text($"ReSharper's rename of {_rename} reported that it failed.", "Changed", []);

   /// <summary>
   ///   The answer for a dry run: what the rename would change, all of which was rolled back, and, with an error conflict,
   ///   that the rename itself would be <see cref="Refused"/>.
   /// </summary>
   public string Previewed() =>
      Text($"Would rename {_rename}: a dry run, which changed nothing and renamed no file."
         + (HasAnErrorConflict ? " For the error conflict, the rename would be refused unless applyDespiteConflicts is true." : ""),
           "Would change",
           []);

   /// <summary>The answer for a rename refused for its <see cref="HasAnErrorConflict"/>, and rolled back.</summary>
   public string Refused() =>
      Text($"Didn't rename {_rename}: ReSharper reports an error conflict, such as a usage it can't update correctly, which the rename "
         + "would leave broken. Fix what the conflicts name first, or pass applyDespiteConflicts: true and fix them by hand after.",
           "Would change",
           []);

   /// <summary>
   ///   <paramref name="heading"/>, the conflicts, then the files changed, under <paramref name="verb"/>, each at its
   ///   path after <paramref name="renamedFiles"/>, and what couldn't be read.
   /// </summary>
   string Text(string heading, string verb, IReadOnlyList<RenamedFile> renamedFiles)
   {
      var files = FileLines(renamedFiles);
      var lines = new List<string> { heading };
      lines.AddRange(_conflictLines);
      lines.Add(files.Count == 0 ? $"{verb} no file." : $"{verb} {files.Count} {(files.Count == 1 ? "file" : "files")}:");
      lines.AddRange(files.Select(it => "  " + it));
      if(_unreadable.Line is {} skipped) lines.Add(skipped);
      return string.Join("\n", lines);
   }

   /// <summary>
   ///   Each changed file by its path in the work tree, a renamed one by its new path, saying what it was named before:
   ///   <c>Shapes\Disk.cs, renamed from Circle.cs</c>.
   /// </summary>
   IReadOnlyList<string> FileLines(IReadOnlyList<RenamedFile> renamedFiles)
   {
      var renamedFrom = renamedFiles.ToDictionary(it => it.From, StringComparer.OrdinalIgnoreCase);
      return [.. _changedFiles.Concat(renamedFiles.Select(it => it.From))
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .Select(it => renamedFrom.TryGetValue(it, out var renamed) ? $"{_root.Relative(renamed.To)}, renamed from {Path.GetFileName(renamed.From)}" : _root.Relative(it))
                              .OrderBy(it => it, StringComparer.Ordinal)];
   }

   /// <summary>A conflict's line: <c>error conflict: Containing type has the same name as the new name</c>.</summary>
   string LineOf(IConflict conflict)
   {
      var description = _unreadable.Read(() => "a conflict's description", () => conflict.Description) ?? "[its description couldn't be read]";
      var noLongerValid = _unreadable.Read(() => "whether a conflict is valid", () => (bool?)conflict.IsValid) == false ? " [no longer valid]" : "";
      return $"{conflict.Severity.ToString().ToLowerInvariant()} conflict: {description}{noLongerValid}";
   }

   bool IsAValidError(IConflict conflict) =>
      conflict.Severity == ConflictSeverity.Error && _unreadable.Read(() => "whether a conflict is valid", () => (bool?)conflict.IsValid) != false;
}
