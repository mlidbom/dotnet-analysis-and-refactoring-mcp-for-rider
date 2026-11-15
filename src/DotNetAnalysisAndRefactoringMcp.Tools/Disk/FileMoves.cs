#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Application.Threading;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.Util;

namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   Files an edit moves, moved on disk once Rider has saved the edit, as <c>git mv</c> would, and Rider's project model
///   brought up to date with them before the answer: the next call finds each file where it went.
/// </summary>
/// <remarks>
///   <para>
///     On disk, not through the project model: a file that the project model moved into a folder new to it became a
///     link in an SDK-style project file (<c>&lt;Compile Update="…"&gt;&lt;Link&gt;</c>), as MSBuild's evaluation, from
///     which Rider's model of such a project comes, didn't have the folder yet (2026-11-15). On disk, the project's
///     globbing takes the file in where it is.
///   </para>
///   <para>
///     After the save: a file moved with an edit Rider hasn't written would leave the edit behind, so such a file isn't
///     moved.
///   </para>
/// </remarks>
sealed class FileMoves(ISolution solution, IShellLocks locks)
{
   static readonly TimeSpan HowLongRiderMayTakeToSeeIt = TimeSpan.FromSeconds(10);
   static readonly TimeSpan BetweenChecks = TimeSpan.FromMilliseconds(100);

   readonly ISolution _solution = solution;
   readonly IShellLocks _locks = locks;

   /// <summary>
   ///   Makes <paramref name="moves"/>, except those of files in <paramref name="notSaved"/>, and waits until Rider's
   ///   project model has each file where it went. What became of each, for the answer.
   /// </summary>
   public async Task<List<string>> Make(IReadOnlyList<FileMove> moves, IReadOnlyList<DocumentNotSaved> notSaved, Lifetime lifetime, McpToolExecution execution, SolutionRoot root)
   {
      var outcomes = new List<string>();
      var made = new List<FileMove>();
      foreach(var move in moves)
      {
         if(notSaved.Any(it => string.Equals(it.File, move.From, StringComparison.OrdinalIgnoreCase)))
            outcomes.Add($"The file wasn't moved to {root.Relative(move.To)}: Rider hadn't saved it.");
         else if(File.Exists(move.To))
            outcomes.Add($"The file wasn't moved: {root.Relative(move.To)} is there already.");
         else
         {
            Directory.CreateDirectory(Path.GetDirectoryName(move.To)!);
            File.Move(move.From, move.To);
            made.Add(move);
         }
      }

      if(made.Count == 0)
         return outcomes;

      await new Rescans(_solution, _locks).Request(lifetime, [.. made.SelectMany(it => new[] { it.From, it.To, Path.GetDirectoryName(it.From)!, Path.GetDirectoryName(it.To)! }).Distinct()])
                                        .ConfigureAwait(false);
      var unseen = await WhatRiderDoesntSeeYet(made, lifetime, execution).ConfigureAwait(false);
      outcomes.AddRange(made.Select(it => unseen.Contains(it)
                                             ? $"Moved the file to {root.Relative(it.To)}, which Rider's project model hadn't taken in within {HowLongRiderMayTakeToSeeIt.TotalSeconds:0} s."
                                             : $"Moved the file to {root.Relative(it.To)}."));
      return outcomes;
   }

   /// <summary>Those of <paramref name="made"/> that Rider's project model doesn't yet have where they went.</summary>
   /// <remarks>
   ///   Not also gone from where they were: in 1 of 80 moves the model had the file at its new path at once, and still
   ///   at its old one 10 s later, though it had let it go when asked a minute after (2026-11-15). The next call needs the
   ///   new path.
   /// </remarks>
   async Task<List<FileMove>> WhatRiderDoesntSeeYet(List<FileMove> made, Lifetime lifetime, McpToolExecution execution)
   {
      var deadline = DateTime.UtcNow + HowLongRiderMayTakeToSeeIt;
      while(true)
      {
         var unseen = await execution.Read(() => made.Where(it => !IsInTheProjectModel(it.To)).ToList()).ConfigureAwait(false);
         if(unseen.Count == 0 || DateTime.UtcNow >= deadline)
            return unseen;
         await Task.Delay(BetweenChecks, lifetime).ConfigureAwait(false);
      }
   }

   bool IsInTheProjectModel(string file) =>
      _solution.FindProjectItemsByLocation(VirtualFileSystemPath.Parse(file, InteractionContext.SolutionContext)).OfType<IProjectFile>().Any();
}
