using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;
using Xunit;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithAPeer;

[Collection(nameof(WithThePeerRider))]
public class When_a_call_names_this_Riders_solution_by_its_path_segment(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer =
      sandbox.McpServer.CallTool("search_symbol",
                                 new { query = "OnlyInThePeer", solutionName = sandbox.McpServer.UniquePathSegmentOf(sandbox.PathOf(TheSandboxWithTheFixtureSolution.SolutionFile)) });

   /// <summary>The peer runs, with the type in its copy.</summary>
   [XF] public void this_Rider_answers_it_from_its_copy_which_lacks_the_peers_type() => _answer.Must().Satisfy(it => !it.Contains("class OnlyInThePeer"));
}
