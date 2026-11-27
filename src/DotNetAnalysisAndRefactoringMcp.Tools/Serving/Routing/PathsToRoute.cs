#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving.Routing;

/// <summary>
///   The absolute files and folders a call names, to choose the solution it goes to by how well each fits them
///   (<see cref="SolutionFit"/>): the projects they are in, found once for all the solutions compared.
/// </summary>
sealed class PathsToRoute(IReadOnlyList<string> absolutePaths)
{
   readonly IReadOnlyList<ProjectFolder> _projects = absolutePaths.Select(ProjectFolder.Of).OfType<ProjectFolder>().ToList();
   readonly Dictionary<string, string?> _repositorySolutions = new(FilePaths.Comparer);

   /// <summary>How well the solution file at the absolute <paramref name="solutionFile"/>, in <paramref name="workTree"/>, fits them.</summary>
   public SolutionFit FitOf(string solutionFile, string workTree)
   {
      var solution = SolutionFile.Read(solutionFile);
      var listsTheirProjects = _projects.All(solution.Lists);
      var isTheRepositorys = listsTheirProjects && RepositorySolutionOf(workTree) is {} named && FilePaths.Same(named, solutionFile);
      var projectsListed = listsTheirProjects && _projects.Count > 0 ? solution.ProjectCount : 0;
      return new SolutionFit(isTheRepositorys, listsTheirProjects, ClosenessOf(Path.GetDirectoryName(solutionFile)!), projectsListed);
   }

   /// <summary>How closely <paramref name="folder"/> holds them all: by the length of its path, so a deeper one more closely; not at all when one lies outside it.</summary>
   int ClosenessOf(string folder) => absolutePaths.All(it => FilePaths.IsUnder(it, folder)) ? folder.Length : 0;

   string? RepositorySolutionOf(string workTree)
   {
      if(!_repositorySolutions.TryGetValue(workTree, out var solution))
         _repositorySolutions[workTree] = solution = RepositorySolution.Of(workTree);
      return solution;
   }
}
