using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;
using Xunit;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithAPeer;

/// <summary><c>GET /health</c> on the first sandbox's server, which asks the peer how its solution is.</summary>
[Collection(nameof(WithThePeerRider))]
public class When_asking_for_the_health(TheSandboxWithTheFixtureSolution sandbox, ThePeerRider peer)
{
   readonly JsonElement _health = sandbox.McpServer.Health();
   readonly string _peersSolution = peer.SolutionPath;

   JsonElement ThePeersSolution =>
      _health.GetProperty("solutions").EnumerateArray()
             .Single(it => string.Equals(it.GetProperty("path").GetString(), _peersSolution, StringComparison.OrdinalIgnoreCase));

   [XF] public void it_says_the_peers_solution_is_not_indexing() => ThePeersSolution.GetProperty("indexing").GetBoolean().Must().Be(false);

   /// <summary>Only the peer knows that: it answers for itself.</summary>
   [XF] public void it_says_which_tools_build_serves_the_peers_solution() =>
      ThePeersSolution.GetProperty("tools").GetProperty("build").GetString().Must().Satisfy(it => !string.IsNullOrEmpty(it));
}
