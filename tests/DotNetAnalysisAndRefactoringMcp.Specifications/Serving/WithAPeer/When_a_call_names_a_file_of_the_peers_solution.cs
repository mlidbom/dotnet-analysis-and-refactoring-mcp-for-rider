using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;
using Xunit;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithAPeer;

[Collection(nameof(WithThePeerRider))]
public class When_a_call_names_a_file_of_the_peers_solution(TheSandboxWithTheFixtureSolution sandbox, ThePeerRider peer)
{
   readonly string _answer = sandbox.McpServer.CallTool("list_symbols_in_file", new { filePath = peer.PathOf("Shapes/OnlyInThePeer.cs") });

   [XF] public void the_peer_answers_it() => _answer.Must().Contain("OnlyInThePeer");
}
