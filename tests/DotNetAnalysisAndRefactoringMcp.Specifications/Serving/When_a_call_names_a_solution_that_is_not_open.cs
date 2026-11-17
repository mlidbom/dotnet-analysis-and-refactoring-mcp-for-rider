using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public class When_a_call_names_a_solution_that_is_not_open(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _refusal = sandbox.McpServer.Refusal("search_symbol", new { query = "Circle", solutionName = "NoSuchSolution" });

   [XF] public void it_is_refused_as_not_found() => _refusal.Must().Contain("Solution 'NoSuchSolution' not found");

   [XF] public void the_refusal_lists_the_open_solutions() => _refusal.Must().Contain($"'{TheSandboxWithTheFixtureSolution.SolutionName}'");
}
