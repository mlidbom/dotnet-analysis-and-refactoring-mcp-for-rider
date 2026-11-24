using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   An agent's line numbers can be out of date. On 2026-11-24 get_symbol_source at a column past the end of a doc
///   comment's line answered only that ReSharper failed with an ArgumentOutOfRangeException, and the agent used grep.
/// </summary>
public class When_a_symbols_position_is_past_the_end_of_its_line(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_symbol_source", new { filePath = sandbox.PathOf("Shapes/Circle.cs"), line = 1, column = 60 });

   [XF] public void it_says_the_column_is_outside_the_line_and_quotes_the_line() =>
      _answer.Must().Satisfy(it => it.Contains("Column 60 is outside line 1") && it.Contains("namespace Shapes"));
}
