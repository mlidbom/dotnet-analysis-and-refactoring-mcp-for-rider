using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;
using Xunit;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithAPeer;

[Collection(nameof(WithThePeerRider))]
public class When_a_call_names_the_peers_solution_by_its_path_segment(TheSandboxWithTheFixtureSolution sandbox, ThePeerRider peer)
{
   readonly string _answer =
      sandbox.McpServer.CallTool("search_symbol", new { query = "OnlyInThePeer", solutionName = sandbox.McpServer.UniquePathSegmentOf(peer.SolutionPath) });

   [XF] public void the_peer_answers_it_from_its_own_copy() => _answer.Must().Contain("class OnlyInThePeer");
}
