using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   Palette.Pick is declared on line 9 of Palette.cs, under its doc comment on line 8, indented by two spaces. Until
///   2026-11-24 get_symbol_source answered with its source in a JSON string, every newline and quote escaped, without
///   line numbers, and its first line cut at the doc comment's first column.
/// </summary>
public class When_asking_for_the_source_of_a_method(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _source = sandbox.McpServer.CallTool("get_symbol_source", new { symbolName = "Shapes.Palette.Pick" });

   [XF] public void it_starts_with_the_methods_name_its_file_and_its_lines() =>
      _source.Must().StartWith($"Shapes.Palette.Pick — {sandbox.PathOf("Shapes/Palette.cs")}:8-9\n");

   [XF] public void each_whole_line_comes_after_its_number_as_a_file_read_shows_it() =>
      _source.Must().EndWith("\n8\t  /// <summary>A generic method with a doc comment, so its type parameter's line differs from its declaration's.</summary>"
                           + "\n9\t  public TColor Pick<TColor>(TColor fallback) => fallback;");
}
