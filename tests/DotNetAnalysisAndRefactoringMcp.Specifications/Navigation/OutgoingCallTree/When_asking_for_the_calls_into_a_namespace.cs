using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.OutgoingCallTree;

public class When_asking_for_the_calls_into_a_namespace(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _calls = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Shapes.Drawing.Sketch.Draw", direction = "outgoing", namespaces = new[] { "Shapes.Drawing" } });

   [XF] public void the_calls_into_it_are_listed() => _calls.Must().Contain("\n  Shapes.Drawing.Sketch.Caption  Shapes.Drawing\\Sketch.cs:30\n");

   [XF] public void the_calls_into_the_solutions_code_outside_it_are_not() => _calls.Must().Satisfy(it => !it.Contains("Shapes.Outline"));

   [XF] public void but_they_are_counted() => _calls.Must().Contain(" calls outside Shapes.Drawing.");
}
