using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.IncomingCallTree;

/// <summary>
///   DetailedSketch.Describe overrides Sketch.Describe, which Sketch.Draw calls on itself, and calls it through
///   <c>base.Describe()</c>.
/// </summary>
public class When_asking_who_calls_an_override(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Shapes.Drawing.DetailedSketch.Describe", direction = "incoming" });

   [XF] public void it_starts_at_the_override_saying_that_calls_through_the_member_it_overrides_are_included() =>
      _answer.Must().Contain("\nShapes.Drawing.DetailedSketch.Describe  Shapes.Drawing\\DetailedSketch.cs:6  (includes calls through Shapes.Drawing.Sketch.Describe)\n");

   [XF] public void it_lists_a_call_of_the_member_it_overrides_which_may_run_it() =>
      _answer.Must().Contain("\n  Shapes.Drawing.Sketch.Draw  Shapes.Drawing\\Sketch.cs:17  (through Shapes.Drawing.Sketch.Describe)\n    22: return $\"{nameof(");

   [XF] public void it_leaves_out_its_own_call_through_base_which_never_runs_it() =>
      _answer.Must().Satisfy(it => !it.Contains("\n  Shapes.Drawing.DetailedSketch.Describe  "));
}
