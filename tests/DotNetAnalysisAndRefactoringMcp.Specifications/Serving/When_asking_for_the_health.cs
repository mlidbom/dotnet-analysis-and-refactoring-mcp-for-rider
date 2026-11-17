using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

/// <summary><c>GET /health</c>, the readiness check that session hooks and the development tools wait on.</summary>
public class When_asking_for_the_health(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly JsonElement _health = sandbox.McpServer.Health();

   JsonElement TheFixtureSolution =>
      _health.GetProperty("solutions").EnumerateArray().Single(it => it.GetProperty("name").GetString() == TheSandboxWithTheFixtureSolution.SolutionName);

   [XF] public void it_lists_the_fixture_solution_with_its_path() => TheFixtureSolution.GetProperty("path").GetString().Must().Contain(TheSandboxWithTheFixtureSolution.SolutionFile);

   [XF] public void it_says_the_fixture_solution_is_not_indexing() => TheFixtureSolution.GetProperty("indexing").GetBoolean().Must().Be(false);

   [XF] public void it_says_no_solution_is_indexing() => _health.GetProperty("indexing").GetBoolean().Must().Be(false);

   [XF] public void it_says_which_tools_build_serves_the_fixture_solution() =>
      TheFixtureSolution.GetProperty("tools").GetProperty("build").GetString().Must().Satisfy(it => !string.IsNullOrEmpty(it));
}
