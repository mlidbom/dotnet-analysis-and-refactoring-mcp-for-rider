using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

public class When_finding_the_usages_of_an_interface_method(TheSandboxWithTheFixtureSolution sandbox)
{
  readonly string _answer = sandbox.McpServer.CallTool("find_usages", new { symbolName = "IShape.Area" });

  [XF] public void it_finds_the_usage_in_the_same_project() => _answer.Must().Contain("ShapeExtensions.cs");
  [XF] public void and_the_one_in_the_project_that_references_it() => _answer.Must().Contain("Drawing.cs");
}
