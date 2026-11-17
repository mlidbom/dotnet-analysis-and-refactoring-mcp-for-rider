using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;
using Xunit;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithAPeer;

[Collection(nameof(WithThePeerRider))]
public class When_a_call_names_the_name_both_solutions_share(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _refusal = sandbox.McpServer.Refusal("search_symbol", new { query = "Circle", solutionName = TheSandboxWithTheFixtureSolution.SolutionName });

   /// <summary>Both copies of the fixture solution are named after its file.</summary>
   [XF] public void it_is_refused_as_ambiguous() => _refusal.Must().Contain($"Ambiguous solution name '{TheSandboxWithTheFixtureSolution.SolutionName}'");

   [XF] public void the_refusal_says_which_path_segment_names_each() => _refusal.Must().Contain("use solutionName:");
}
