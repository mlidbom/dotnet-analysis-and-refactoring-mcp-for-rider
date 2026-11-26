using Compze.Must;
using Compze.xUnitBDD;
using static DotNetAnalysisAndRefactoringMcp.Specifications.TheFixtureSourceGenerator;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.GeneratedCode;

/// <summary>
///   find_usages of Shapes.Ruler.Doubled, which <see cref="TheFixtureSourceGenerator"/> writes, and which Swatch.Describe
///   in Shapes/Swatch.cs calls, and the generated Swatch.Shade too. Until 2026-11-26 the usages in generated code were
///   left out.
/// </summary>
public class When_finding_the_usages_of_generated_code(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _usagesOfAWhollyGeneratedTypesMember = sandbox.McpServer.CallTool("find_usages", new { symbolName = "Shapes.Ruler.Doubled" }).ReplaceLineEndings("\n");
   readonly string _usagesOfAMemberGeneratedIntoAPartialType = sandbox.McpServer.CallTool("find_usages", new { symbolName = "Shapes.Swatch.Shade" }).ReplaceLineEndings("\n");

   [XF] public void it_counts_the_usages_in_generated_code_with_the_others() =>
      _usagesOfAWhollyGeneratedTypesMember.Must().StartWith("method Doubled — 2 usages in 2 files, 1 projects\n");

   [XF] public void a_usage_in_generated_code_is_under_its_generated_file_named_with_its_generator() =>
      _usagesOfAWhollyGeneratedTypesMember.Must().Contain($"\n{FileNamed("Swatch.g.cs")}\n  6: public int Shade() => Ruler.Doubled(25);");

   [XF] public void a_usage_in_a_file_of_the_solution_is_under_its_path() =>
      _usagesOfAWhollyGeneratedTypesMember.Must().Contain("\nShapes\\Swatch.cs\n  12: public string Describe() => $\"{Color} at {Shade()} of {Ruler.Doubled(50)}\";\n");

   [XF] public void a_member_of_a_wholly_generated_type_is_declared_in_its_generated_file() =>
      _usagesOfAWhollyGeneratedTypesMember.Must().Contain($"\ndeclared: {FileNamed("Ruler.g.cs")}:6\n");

   [XF] public void a_member_generated_into_a_partial_type_is_declared_in_its_generated_file_not_in_the_types_other_part() =>
      _usagesOfAMemberGeneratedIntoAPartialType.Must().Contain($"\ndeclared: {FileNamed("Swatch.g.cs")}:6\n");
}
