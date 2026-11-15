using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;
using Xunit;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithAPeer;

[Collection(nameof(WithThePeerRider))]
public class When_a_call_names_no_solution_and_no_file(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _refusal = sandbox.McpServer.Refusal("search_symbol", new { query = "Circle" });

   /// <summary>The peer runs, with its copy of the fixture solution: two solutions are open.</summary>
   [XF] public void it_is_refused_for_several_solutions_being_open() => _refusal.Must().Contain("Multiple solutions are open");
}
