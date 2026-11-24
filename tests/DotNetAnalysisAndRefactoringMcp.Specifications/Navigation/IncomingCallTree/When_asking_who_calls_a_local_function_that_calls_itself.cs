using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.IncomingCallTree;

/// <summary>Nesting, a local function in Sketch.Draw, which Draw calls and which calls itself.</summary>
public class When_asking_who_calls_a_local_function_that_calls_itself(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_call_hierarchy", new { filePath = sandbox.PathOf("Shapes.Drawing/Sketch.cs"), line = 24, column = 9, direction = "incoming" });

   [XF] public void it_lists_the_member_it_is_declared_in_which_calls_it() =>
      _answer.Must().Contain("\n  Shapes.Drawing.Sketch.Draw  Shapes.Drawing\\Sketch.cs:17\n    22: return $\"{nameof(");

   [XF] public void it_lists_itself_as_a_caller_marked_recursive_with_the_line_of_the_call() =>
      _answer.Must().Contain("\n  Shapes.Drawing.Sketch.Draw.Nesting  Shapes.Drawing\\Sketch.cs:24  (recursive)\n    24: int Nesting(int from) => from >= _shapes.Length ? 0 : 1 + Nesting(from + 1);");
}
