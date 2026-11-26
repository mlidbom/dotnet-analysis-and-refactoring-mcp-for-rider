using Compze.Must;
using Compze.xUnitBDD;
using static DotNetAnalysisAndRefactoringMcp.Specifications.TheFixtureSourceGenerator;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.GeneratedCode;

/// <summary>
///   go_to_definition of what <see cref="TheFixtureSourceGenerator"/> writes, which no file with a path declares: until
///   2026-11-26 it said only "[generated]", not which generator nor where in its file.
/// </summary>
public class When_going_to_the_definition_of_generated_code(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _whollyGeneratedType = sandbox.McpServer.CallTool("go_to_definition", new { symbolName = "Shapes.Ruler" });
   readonly string _memberGeneratedIntoAPartialType = sandbox.McpServer.CallTool("go_to_definition", new { symbolName = "Shapes.Swatch.Shade" });

   [XF] public void a_wholly_generated_type_is_in_its_generated_file_named_with_its_generator() =>
      _whollyGeneratedType.Must().StartWith($"static class Ruler — {FileNamed("Ruler.g.cs")}:4:21\n");

   /// <summary>The type's other part is in Shapes/Swatch.cs: until 2026-11-26 the text forms said it was there.</summary>
   [XF] public void a_member_generated_into_a_partial_type_is_in_its_generated_file_not_in_the_types_other_part() =>
      _memberGeneratedIntoAPartialType.Must().StartWith($"method Shade() : int — {FileNamed("Swatch.g.cs")}:6:15\n");

   [XF] public void its_lines_are_numbered_as_in_the_generated_file() =>
      _memberGeneratedIntoAPartialType.Must().Contain("\n6\t   public int Shade() => Ruler.Doubled(25);");
}
