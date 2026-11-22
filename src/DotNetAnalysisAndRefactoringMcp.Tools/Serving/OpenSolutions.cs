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

   SolutionRouting<OpenSolution> Routing => new(All);

   public (OpenSolution Chosen, string Refusal) Choose(string solutionName, JObject arguments) =>
      Routing.Choose(solutionName, FileArguments.Of(arguments));

   public JObject Listed() => Routing.Listed();

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
}
