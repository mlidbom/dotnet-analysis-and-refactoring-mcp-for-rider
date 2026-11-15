using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Inspections;

/// <summary>A tuple has no Deconstruct method: until 2026-11-15 each of its deconstructions was reported as an unresolved 'Deconstruct'.</summary>
public class When_checking_a_file_that_deconstructs_tuples(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_file_errors", new { filePath = sandbox.PathOf("Shapes/Bounds.cs") });

   [XF] public void it_reports_none() => _answer.Must().Contain("0 diagnostics");
}
