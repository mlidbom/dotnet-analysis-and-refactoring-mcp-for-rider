using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Inspections;

/// <summary>The whole-solution sweep that took jb inspectcode before inspect_code (2026-11-15).</summary>
public class When_inspecting_the_solution(TheSandboxWithTheFixtureSolution sandbox)
{
  static readonly TimeSpan HowLongSolutionWideAnalysisMayTake = TimeSpan.FromMinutes(1);

  readonly string _answer = OnceSolutionWideAnalysisIsIn(() => sandbox.McpServer.CallTool("inspect_code", new { minSeverity = "hint" }));

  [XF] public void it_finds_what_the_file_daemon_finds() => _answer.Must().Contain("NotAccessedVariable.Compiler");

  [XF] public void and_what_only_solution_wide_analysis_finds() => _answer.Must().Contain("UnusedMember.Global");

  [XF] public void it_counts_the_issues_per_inspection() => _answer.Must().Contain("by inspection:");

  /// <summary>A fresh copy of the fixture solution is analysed solution-wide in the background after it opens.</summary>
  static string OnceSolutionWideAnalysisIsIn(Func<string> inspect)
  {
    var deadline = DateTime.UtcNow + HowLongSolutionWideAnalysisMayTake;
    while (true)
    {
      var answer = inspect();
      if (answer.Contains("With solution-wide analysis") || DateTime.UtcNow > deadline) return answer;
      Thread.Sleep(1000);
    }
  }
}

public class When_inspecting_a_file_for_one_inspection(TheSandboxWithTheFixtureSolution sandbox)
{
  readonly string _answer = sandbox.McpServer.CallTool("inspect_code", new { filePath = "Shapes.Drawing/Exporting.cs", minSeverity = "hint", inspectionIds = new[] { "NotAccessedVariable.Compiler" } });

  [XF] public void it_finds_that_inspection() => _answer.Must().Contain("'neverRead'");

  [XF] public void and_no_other() => _answer.Must().NotContain("UnusedMember.Global:");

  [XF] public void it_inspects_only_that_file() => _answer.Must().Contain("inspected 1 files");
}
