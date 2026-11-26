#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using DotNetAnalysisAndRefactoringMcp.Tools;
using JetBrains.ReSharper.Psi;

namespace DotNetAnalysisAndRefactoringMcp.Renames;

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
   readonly RenameRequest _request;

   readonly RenameConflicts _conflicts;

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

   readonly SolutionRoot _root;

   /// <summary>
   ///   The outcome of <paramref name="request"/>: its <paramref name="conflicts"/>, and
   ///   <paramref name="changedFiles"/>. Read inside the rename's transaction, before a rollback.
   /// </summary>
   public RenameOutcome(RenameRequest request, RenameConflicts conflicts, IEnumerable<IPsiSourceFile> changedFiles, SolutionRoot root)
   {
      _request = request;
      _conflicts = conflicts;
      _changedFiles = [.. changedFiles.Select(it => it.GetLocation().FullPath).Where(it => it.Length > 0).Distinct()];
      _root = root;
   }

   /// <summary>
   ///   Whether the rename is refused, and rolled back, for an error conflict (<see cref="RenameConflicts.HasAnError"/>):
   ///   unless the request is a dry run, or applies the rename despite conflicts.
   /// </summary>
   public bool IsRefused => !_request.IsADryRun && !_request.AppliesDespiteConflicts && _conflicts.HasAnError;

   /// <summary>The answer for a rename that was applied, and renamed <paramref name="renamedFiles"/>.</summary>
   public string Applied(IReadOnlyList<RenamedFile> renamedFiles) => Text($"Renamed {_request}.", "Changed", renamedFiles);

   /// <summary>The answer for a rename ReSharper's refactoring reported failing: what it changed nonetheless.</summary>
   public string NotCompleted() => Text($"ReSharper's rename of {_request} reported that it failed.", "Changed", []);

   /// <summary>
   ///   The answer for a dry run: what the rename would change, all of which was rolled back, and, with an error conflict,
   ///   that the rename itself would be <see cref="Refused"/>.
   /// </summary>
   public string Previewed() =>
      Text($"Would rename {_request}: a dry run, which changed nothing and renamed no file."
         + (_conflicts.HasAnError ? " For the error conflict, the rename would be refused unless applyDespiteConflicts is true." : ""),
           "Would change",
           []);

   /// <summary>The answer for a rename that <see cref="IsRefused"/>, and was rolled back.</summary>
   public string Refused() =>
      Text($"Didn't rename {_request}: ReSharper reports an error conflict, such as a usage it can't update correctly, which the rename "
         + "would leave broken. Fix what the conflicts name first, or pass applyDespiteConflicts: true and fix them by hand after.",
           "Would change",
           []);

   /// <summary>
   ///   <paramref name="heading"/>, the conflicts, then the files changed, under <paramref name="verb"/>, each at its
   ///   path after <paramref name="renamedFiles"/>, and what of the conflicts couldn't be read.
   /// </summary>
   string Text(string heading, string verb, IReadOnlyList<RenamedFile> renamedFiles)
   {
      var files = FileLines(renamedFiles);
      var filesHeading = files.Count == 0 ? $"{verb} no file." : $"{verb} {files.Count} {(files.Count == 1 ? "file" : "files")}:";
      List<string> lines = [heading, .. _conflicts.Lines, filesHeading, .. files.Select(it => "  " + it)];
      if(_conflicts.UnreadableLine is {} unreadable) lines.Add(unreadable);
      return string.Join("\n", lines);
   }

   /// <summary>
   ///   Each changed file by its path in the work tree, a renamed one by its <see cref="RenamedFile.LineIn"/>:
   ///   <c>Shapes\Disk.cs, renamed from Circle.cs</c>.
   /// </summary>
   IReadOnlyList<string> FileLines(IReadOnlyList<RenamedFile> renamedFiles)
   {
      var renamedFrom = renamedFiles.ToDictionary(it => it.From, StringComparer.OrdinalIgnoreCase);
      return [.. _changedFiles.Concat(renamedFiles.Select(it => it.From))
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .Select(it => renamedFrom.TryGetValue(it, out var renamed) ? renamed.LineIn(_root) : _root.Relative(it))
                              .OrderBy(it => it, StringComparer.Ordinal)];
   }
}
