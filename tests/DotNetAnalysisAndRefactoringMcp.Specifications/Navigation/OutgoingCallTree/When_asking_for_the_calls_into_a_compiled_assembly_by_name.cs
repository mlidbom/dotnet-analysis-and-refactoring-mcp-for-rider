using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.OutgoingCallTree;

public class When_asking_for_the_calls_into_a_compiled_assembly_by_name(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _calls = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Shapes.Drawing.Sketch.Draw", direction = "outgoing", assemblies = new[] { "System.Linq" } });

   [XF] public void the_calls_into_it_are_listed_with_the_assembly_for_where_they_are() =>
      _calls.Must().Contain("\n  System.Linq.Enumerable.ToList  [assembly System.Linq.dll]\n");

   [XF] public void the_heading_names_it() => _calls.Must().StartWith("Calls into the solution's code and System.Linq, 2 levels deep:");
}
