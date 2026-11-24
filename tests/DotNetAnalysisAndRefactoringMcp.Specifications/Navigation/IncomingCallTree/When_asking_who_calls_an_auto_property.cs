using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.IncomingCallTree;

/// <summary>
///   Sketch.Title, an auto-property: its accessors run no code of their own, but the code that reads it is what an
///   agent asks for. Sketch.Describe reads it, and DetailedSketch.Describe calls that through <c>base.Describe()</c>.
/// </summary>
public class When_asking_who_calls_an_auto_property(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Shapes.Drawing.Sketch.Title", direction = "incoming" });

   [XF] public void it_lists_the_code_that_reads_it() =>
      _answer.Must().Contain("\n  Shapes.Drawing.Sketch.Describe  Shapes.Drawing\\Sketch.cs:28\n    28: public virtual string Describe() => new Palette().Mix(\"grey\", Title);\n");

   [XF] public void a_call_through_base_is_a_call_of_the_member_it_names() =>
      _answer.Must().Contain("\n    Shapes.Drawing.DetailedSketch.Describe  Shapes.Drawing\\DetailedSketch.cs:6\n      6: public override string Describe() => base.Describe() + \" in detail\";\n");
}
