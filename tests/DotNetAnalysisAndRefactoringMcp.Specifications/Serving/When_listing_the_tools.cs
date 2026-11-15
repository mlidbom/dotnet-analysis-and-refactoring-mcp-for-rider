using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public class When_listing_the_tools(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly IReadOnlyList<JsonElement> _tools = sandbox.McpServer.ListTools();

   IEnumerable<string> Names => _tools.Select(it => it.GetProperty("name").GetString()!);

   [XF] public void list_solutions_comes_first() => Names.First().Must().Be("list_solutions");

   [XF] public void it_lists_the_solutions_tools() => Names.ToList().Must().Satisfy(it => it.Contains("find_usages") && it.Contains("rename_symbol"));

   [XF] public void every_tool_but_list_solutions_takes_the_solution_name_that_routes_a_call() =>
      _tools.Skip(1)
            .Where(it => !it.GetProperty("inputSchema").GetProperty("properties").TryGetProperty("solutionName", out _))
            .Select(it => it.GetProperty("name").GetString())
            .ToList()
            .Must().Satisfy(it => it.Count == 0);
}
