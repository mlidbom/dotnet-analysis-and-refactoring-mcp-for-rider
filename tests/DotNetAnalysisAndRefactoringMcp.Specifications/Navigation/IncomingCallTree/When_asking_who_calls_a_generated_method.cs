using Compze.Must;
using Compze.xUnitBDD;
using static DotNetAnalysisAndRefactoringMcp.Specifications.TheFixtureSourceGenerator;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.IncomingCallTree;

/// <summary>
///   get_call_hierarchy's incoming tree of Shapes.Ruler.Doubled, which <see cref="TheFixtureSourceGenerator"/> writes:
///   Swatch.Describe in Shapes/Swatch.cs calls it, and so does Swatch.Shade, which the generator writes into Swatch too.
///   Until 2026-11-26 a call in generated code was on a line of no file, ":6:".
/// </summary>
public class When_asking_who_calls_a_generated_method(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _callers = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Shapes.Ruler.Doubled", direction = "incoming" });

   [XF] public void it_starts_at_the_method_in_its_generated_file_named_with_its_generator() =>
      _callers.Must().StartWith($"Callers, 2 levels deep:\n\nShapes.Ruler.Doubled  {FileNamed("Ruler.g.cs")}:6\n");

   [XF] public void a_generated_caller_is_in_its_generated_file_with_the_line_there_that_calls() =>
      _callers.Must().Contain($"\n  Shapes.Swatch.Shade  {FileNamed("Swatch.g.cs")}:6\n    6: public int Shade() => Ruler.Doubled(25);\n");

   [XF] public void and_its_own_callers_are_followed_into_the_files_of_the_solution() =>
      _callers.Must().Contain("\n    Shapes.Swatch.Describe  Shapes\\Swatch.cs:12\n      12: public string Describe() => $\"{Color} at {Shade()} of {Ruler.Doubled(50)}\";\n");
}
