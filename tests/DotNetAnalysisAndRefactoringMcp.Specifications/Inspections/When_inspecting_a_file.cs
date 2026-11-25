using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Inspections;

public class When_inspecting_a_file(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_diagnostics", new { filePath = sandbox.PathOf("Shapes.Drawing/Exporting.cs"), minSeverity = "hint" });

   [XF] public void it_reports_the_variable_that_is_never_read() => _answer.Must().Contain("neverRead");

   [XF] public void it_heads_the_answer_with_the_file_and_how_many_issues_at_which_severity() =>
      _answer.Must().StartWith("Shapes.Drawing\\Exporting.cs — 1 issue at hint or above\n");

   [XF] public void it_writes_each_issue_on_a_line_of_its_own_from_its_position_and_severity_to_a_mark_when_a_quick_fix_is_available() =>
      _answer.Split('\n').Must().Satisfy(lines => lines.Any(it => it.StartsWith("  9:9 warning ") && it.Contains("neverRead") && it.EndsWith("  (quick-fix)")));
}
