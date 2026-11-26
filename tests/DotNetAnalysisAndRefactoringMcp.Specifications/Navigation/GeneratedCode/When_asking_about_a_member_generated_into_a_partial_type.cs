using Compze.Must;
using Compze.xUnitBDD;
using static DotNetAnalysisAndRefactoringMcp.Specifications.TheFixtureSourceGenerator;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.GeneratedCode;

/// <summary>
///   get_symbol_info and get_symbol_source of Shapes.Swatch.Shade, which <see cref="TheFixtureSourceGenerator"/> writes
///   into the type whose other part Shapes/Swatch.cs declares. Until 2026-11-26 get_symbol_info said nothing of where it
///   is declared, and get_symbol_source said "[no file]".
/// </summary>
public class When_asking_about_a_member_generated_into_a_partial_type(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _information = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "Shapes.Swatch.Shade" });
   readonly string _source = sandbox.McpServer.CallTool("get_symbol_source", new { symbolName = "Shapes.Swatch.Shade" });

   [XF] public void its_information_says_it_is_declared_in_its_generated_file() =>
      _information.Must().Contain($"\ndeclared: {FileNamed("Swatch.g.cs")}:6:15\n");

   [XF] public void its_source_is_headed_by_its_generated_file_and_its_lines_there() =>
      _source.Must().StartWith($"Shapes.Swatch.Shade — {FileNamed("Swatch.g.cs")}:5-6\n");
}
