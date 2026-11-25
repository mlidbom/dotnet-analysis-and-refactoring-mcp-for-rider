using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   Palette.Pick is declared on line 9 of Palette.cs, under its doc comment on line 8. Until 2026-11-25 go_to_definition
///   showed the declaration's first 200 characters, the doc comment's <c>///</c> and tags included, and the lines after
///   the first indented as in the file while the first wasn't.
/// </summary>
public class When_going_to_the_definition_of_a_documented_method(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("go_to_definition", new { symbolName = "Shapes.Palette.Pick" });

   [XF] public void it_gives_the_comments_summary_as_text_then_the_declarations_numbered_lines_after_the_comment() =>
      _answer.Must().EndWith(" — Shapes\\Palette.cs:9:17"
                           + "\ndoc: A generic method with a doc comment, so its type parameter's line differs from its declaration's."
                           + "\n9\t  public TColor Pick<TColor>(TColor fallback) => fallback;");
}
