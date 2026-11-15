using System.Collections.Generic;
using System.IO;
using System.Linq;
using DotNetAnalysisAndRefactoringMcp.Tools;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>
///   The solutions this Rider's MCP server routes calls to, as they were when a request came: this Rider's own, and
///   the ones its peers registered with it.
/// </summary>
sealed class OpenSolutions
{
   OpenSolutions(IReadOnlyList<LocalSolution> local, IReadOnlyList<PeerSolution> peers)
   {
      Local = local;
      Peers = peers;
      All = [..local, ..peers];
   }

   public static OpenSolutions Of(IMcpServer server) =>
      new(server.Solutions.Select(solution => new LocalSolution(server, solution)).ToList(),
          server.Peers.Select(peer => new PeerSolution(server, peer)).ToList());

   /// <summary>The solutions of this Rider.</summary>
   public IReadOnlyList<LocalSolution> Local { get; }

   /// <summary>The solutions the peers registered.</summary>
   public IReadOnlyList<PeerSolution> Peers { get; }

   /// <summary>This Rider's solutions, then the peers'.</summary>
   public IReadOnlyList<OpenSolution> All { get; }

   /// <summary>
   ///   The solution a <c>tools/call</c> goes to: the one <paramref name="solutionName"/> names, or the only one open, or
   ///   the one that holds every file the <paramref name="arguments"/> name. When none fits, a refusal that says why.
   /// </summary>
   public (OpenSolution Chosen, string Refusal) Choose(string solutionName, JObject arguments)
   {
      if(All.Count == 0)
         return (null, "No solutions are currently open in Rider.");

      if(solutionName != null)
         return Named(solutionName);

      if(All.Count == 1)
         return (All[0], null);

      if(FileArguments.Of(arguments) is { Count: > 0 } paths)
         return HoldingEvery(paths);

      return (null, "Multiple solutions are open. Specify 'solutionName' in the arguments, "
                  + "or pass a filePath that exists in only one of them.\n"
                  + $"Available solutions:\n{string.Join("\n", All.Select(it => $"  - {it.Name} ({it.FilePath})"))}");
   }

   /// <summary>The answer of <c>list_solutions</c>: every solution's name, path and tool count, and the segment that picks out one of several of a name.</summary>
   public JObject Listed()
   {
      var segments = UniquePathSegments();
      var solutions = All.Select(solution =>
      {
         var listed = solution.NameAndPath();
         listed["toolCount"] = solution.Tools.Count;
         if(segments.TryGetValue(solution.FilePath, out var segment))
            listed["uniquePathSegment"] = segment;
         return listed;
      }).ToList();

      return new JObject { ["solutionCount"] = solutions.Count, ["solutions"] = new JArray(solutions) };
   }

   /// <summary>
   ///   <c>GET /health</c>: every solution's health, and whether any is indexing or of unknown state, so that a
   ///   readiness check doesn't read it as ready. A peer that doesn't answer at once is reported unreachable.
   /// </summary>
   public JObject Health()
   {
      var solutions = All.Select(solution => solution.Health()).ToList();

      return new JObject
             {
                ["solutions"] = new JArray(solutions),
                ["indexing"] = solutions.Any(it => it["indexing"]?.Value<bool>() == true || it["healthFailed"] != null),
                ["pendingDiskChanges"] = solutions.Sum(it => it["pendingDiskChanges"]?.Value<int>() ?? 0)
             };
   }

   /// <summary>
   ///   The solution <paramref name="solutionName"/> names: by its name or path, or else by whole segments of its path,
   ///   which pick out one of several solutions of a name.
   /// </summary>
   (OpenSolution, string) Named(string solutionName)
   {
      var matches = All.Where(it => it.IsNamed(solutionName)).ToList();
      if(matches.Count != 1)
      {
         var bySegment = All.Where(it => it.HasPathSegment(solutionName)).ToList();
         if(bySegment.Count == 1 || (matches.Count == 0 && bySegment.Count > 0))
            matches = bySegment;
      }

      if(matches.Count == 0)
         return (null, $"Solution '{solutionName}' not found. Available solutions: {string.Join(", ", All.Select(it => $"'{it.Name}'"))}");

      if(matches.Count > 1)
      {
         var segments = UniquePathSegments();
         var listed = string.Join("\n", matches.Select(it => $"  - {it.Name} ({it.FilePath}){Hint(it)}"));
         return (null, $"Ambiguous solution name '{solutionName}'. Matches:\n{listed}");

         string Hint(OpenSolution solution) => segments.TryGetValue(solution.FilePath, out var segment) ? $" — use solutionName: \"{segment}\"" : "";
      }

      return (matches[0], null);
   }

   /// <summary>
   ///   The solution that holds every one of <paramref name="paths"/>, and most closely, by
   ///   <see cref="OpenSolution.Closeness"/>: nested checkouts, and solutions of one work tree, each hold their own files
   ///   most closely. A relative path found in several checkouts names none of them.
   /// </summary>
   (OpenSolution, string) HoldingEvery(IReadOnlyList<string> paths)
   {
      var holders = All.Where(solution => paths.All(solution.Holds))
                       .OrderByDescending(solution => paths.Min(solution.Closeness))
                       .ToList();

      if(holders.Count > 1 && !paths.All(Path.IsPathRooted))
         return (null, $"The relative path exists in several open solutions: {WithWorkTrees(holders)}. Pass 'solutionName' or an absolute path.");

      if(holders.Count == 0)
         return (null, $"Path is outside open solutions: {WithWorkTrees(All)}. Pass 'solutionName' to pick one anyway.");

      return (holders[0], null);

      static string WithWorkTrees(IEnumerable<OpenSolution> solutions) => string.Join(", ", solutions.Select(it => $"{it.Name} ({it.WorkTree})"));
   }

   /// <summary>
   ///   For each solution that shares its name with another, the segment of its path nearest the file that only it
   ///   has, by its file path: what to pass as <c>solutionName</c> to pick it out.
   /// </summary>
   Dictionary<string, string> UniquePathSegments()
   {
      var result = new Dictionary<string, string>();
      foreach(var namesakes in All.GroupBy(it => it.Name, System.StringComparer.OrdinalIgnoreCase).Select(it => it.ToList()).Where(it => it.Count > 1))
      {
         foreach(var solution in namesakes)
         {
            var segment = solution.FilePath.Replace('\\', '/').Split('/')
                                  .Reverse()
                                  .Skip(1) // the file's name
                                  .Where(it => it.Length > 0)
                                  .FirstOrDefault(it => namesakes.Count(other => other.HasPathSegment(it)) == 1);
            if(segment != null)
               result[solution.FilePath] = segment;
         }
      }

      return result;
   }
}
