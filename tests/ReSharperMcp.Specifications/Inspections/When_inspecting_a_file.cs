using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace ReSharperMcp.Specifications.Inspections;

public class When_inspecting_a_file(TheSandboxWithTheFixtureSolution sandbox)
{
  readonly string _answer = sandbox.McpServer.CallTool("get_diagnostics", new { filePath = sandbox.PathOf("Shapes.Drawing/Exporting.cs"), minSeverity = "hint" });

  [XF] public void it_reports_the_variable_that_is_never_read() => _answer.Must().Contain("neverRead");
}
