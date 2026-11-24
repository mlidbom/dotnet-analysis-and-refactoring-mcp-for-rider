using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.OutgoingCallTree;

/// <summary>DetailedSketch.Describe calls base.Describe(), which runs Sketch.Describe itself, never an override of it.</summary>
public class When_a_method_calls_the_member_it_overrides_through_base(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _calls = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Shapes.Drawing.DetailedSketch.Describe", direction = "outgoing" });

   [XF] public void the_tree_lists_the_member_it_overrides_without_its_overrides() =>
      _calls.Must().Satisfy(it => it.Contains("\n  Shapes.Drawing.Sketch.Describe  Shapes.Drawing\\Sketch.cs:28\n") && !it.Contains("override:"));
}
