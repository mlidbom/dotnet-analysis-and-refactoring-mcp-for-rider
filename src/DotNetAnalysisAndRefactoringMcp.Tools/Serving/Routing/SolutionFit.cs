#nullable enable
using System;

namespace DotNetAnalysisAndRefactoringMcp.Serving.Routing;

/// <summary>
///   How well a solution fits the files and folders a call names (<see cref="PathsToRoute"/>), to choose among the
///   solutions of their work tree the one the call goes to, or the one to open for it.
/// </summary>
/// <remarks>
///   One solution fits better than another by the first of these in which they differ:
///   <br/>1. It is the one the repository names (<see cref="RepositorySolution"/>), and lists their projects.
///   <br/>2. It lists their projects (<see cref="ProjectFolder"/>).
///   <br/>3. Its folder holds them all, and more closely: by a deeper folder.
///   <br/>4. It lists more projects, when it lists theirs: what else is called for in the same checkout is then most
///   likely to be in it too.
/// </remarks>
sealed class SolutionFit(bool isTheRepositorys, bool listsTheirProjects, int closeness, int projectsListed) : IComparable<SolutionFit>
{
   (bool, bool, int, int) Rank => (isTheRepositorys, listsTheirProjects, closeness, projectsListed);

   public int CompareTo(SolutionFit? other) => other is null ? 1 : Rank.CompareTo(other.Rank);
}
