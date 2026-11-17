using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public class When_listing_the_open_solutions(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("list_solutions", new {});

   [XF] public void it_lists_the_fixture_solution() => _answer.Must().Contain(TheSandboxWithTheFixtureSolution.SolutionName);
}
