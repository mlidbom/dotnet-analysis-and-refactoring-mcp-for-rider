using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   Drawing's declaration runs from line 6 to line 17 of Drawing.cs, under its doc comment on line 5. Until 2026-11-25
///   go_to_definition showed its first 200 characters, doc comment first, and cut them in the middle of a line.
/// </summary>
public class When_navigating_to_a_long_declaration(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _default = sandbox.McpServer.CallTool("go_to_definition", new { symbolName = "Shapes.Drawing.Drawing" });
   readonly string _explicit = sandbox.McpServer.CallTool("go_to_definition", new { symbolName = "Shapes.Drawing.Drawing", maxTextLength = 200 });
   readonly string _full = sandbox.McpServer.CallTool("go_to_definition", new { symbolName = "Shapes.Drawing.Drawing", maxTextLength = 2000 });

   [XF] public void omitting_the_length_uses_the_advertised_limit() => _default.Must().Be(_explicit);

   [XF] public void the_default_shows_the_whole_lines_that_fit_in_two_hundred_characters_and_says_where_the_declaration_goes_on() =>
      _default.Must().EndWith("\n10\t  public double TotalArea() => _shapes.TotalArea();"
                            + "\n11\t"
                            + "\n… cut at 200 characters: the declaration goes on from line 12 to 17; read the file for the rest.");

   [XF] public void a_longer_limit_shows_every_line() => _full.Must().EndWith("\n16\t  public double ScaledAreaOfTheFirst() => ShapeExtensions.Scaled(AreaOfTheFirst(), 2);\n17\t}");
}
