using JetBrains.Application.PersistentMap;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.Caches;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   Whether Rider's caches of a solution are corrupted: the database its caches are kept in failed, and stores nothing
///   any more until the solution is closed, which deletes it.
/// </summary>
/// <remarks>
///   <para>
///     A desktop Rider then shows a notification asking for a restart (<c>RiderSolutionCachesInvalidator</c>), which
///     nobody sees in Royder's private Rider. Solution-wide analysis keeps the hashes of each file's declarations there,
///     by which it knows which files use a declaration that changed, so it re-analyzes only the changed files themselves
///     after that, and its results for the others stay as they were.
///   </para>
///   <para>
///     On 2026-11-28 the private Rider's caches of two ClaudeTooling worktrees failed, each within three minutes of the
///     solution opening, when another process held a table file its database had just written ("The process cannot
///     access the file because it is being used by another process", in the database's own <c>Db/LOG</c>). For half an
///     hour <c>inspect_code</c> then answered errors from a moment when a file was half written, which the build and
///     the live daemon didn't have. Which process held the file is unknown.
///   </para>
/// </remarks>
static class CorruptedCaches
{
   /// <summary>Whether <paramref name="solution"/>'s caches are corrupted.</summary>
   public static bool In(ISolution solution) =>
      solution.TryGetComponent<SolutionCaches>()?.Db.Status.Value == KeyValueDbStatus.Corrupted;

   /// <summary>What an answer from a solution whose caches are corrupted says of it.</summary>
   public const string Warning =
      "WARNING: Rider's caches of this solution are corrupted, so it stores nothing it learns of the code any more: "
    + "answers can be out of date, solution-wide analysis's most of all. Royder's private Rider opens the solution again, "
    + "with new caches, as soon as no call is using it; a desktop Rider gets new ones when it is restarted.";
}
