using Compze.Must;
using Compze.xUnitBDD;
using static DotNetAnalysisAndRefactoringMcp.Specifications.TheFixtureSourceGenerator;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.OutgoingCallTree;

/// <summary>
///   get_call_hierarchy's outgoing tree from Shapes.Swatch.Describe in Shapes/Swatch.cs, which calls Swatch.Shade and
///   Ruler.Doubled, both of which <see cref="TheFixtureSourceGenerator"/> writes; Shade calls Doubled too.
/// </summary>
public class When_asking_for_the_calls_a_method_makes_into_generated_code(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _calls = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Shapes.Swatch.Describe", direction = "outgoing" });

   [XF] public void generated_code_is_the_solutions_code_and_followed_into_its_generated_files_named_with_their_generator() =>
      _calls.Must().Be("Calls into the solution's code, 2 levels deep:\n\n"
                     + "Shapes.Swatch.Describe  Shapes\\Swatch.cs:12\n"
                     + $"  Shapes.Swatch.Shade  {FileNamed("Swatch.g.cs")}:6\n"
                     + $"    Shapes.Ruler.Doubled  {FileNamed("Ruler.g.cs")}:6\n"
                     + $"  Shapes.Ruler.Doubled  {FileNamed("Ruler.g.cs")}:6");
}
