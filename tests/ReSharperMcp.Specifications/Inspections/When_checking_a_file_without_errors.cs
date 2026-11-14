using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace ReSharperMcp.Specifications.Inspections;

public class When_checking_a_file_without_errors(TheSandboxWithTheFixtureSolution sandbox)
{
  readonly string _answer = sandbox.McpServer.CallTool("get_file_errors", new { filePath = sandbox.PathOf("Shapes/Circle.cs") });

  [XF] public void it_reports_none() => _answer.Must().Contain("0 diagnostics");
}
