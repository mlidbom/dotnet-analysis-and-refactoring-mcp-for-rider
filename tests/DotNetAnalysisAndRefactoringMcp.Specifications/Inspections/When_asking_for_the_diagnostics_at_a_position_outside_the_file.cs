using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Inspections;

/// <summary>A position is checked against the file, and the answer says what's out of range (2026-11-15).</summary>
public class When_asking_for_the_diagnostics_at_a_position_outside_the_file(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_diagnostics",
                                                        new { filePath = sandbox.PathOf("Shapes/Circle.cs"), position = new { line = 999, column = 1 } });

   [XF] public void it_says_the_line_is_outside_the_file() => _answer.Must().Contain("Line 999 is outside the file");
}
