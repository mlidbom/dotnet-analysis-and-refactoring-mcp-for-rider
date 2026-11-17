using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public class When_a_call_names_its_solution(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("search_symbol", new { query = "Circle", solutionName = TheSandboxWithTheFixtureSolution.SolutionName });

   [XF] public void that_solution_answers_it() => _answer.Must().Contain("class Circle");
}
