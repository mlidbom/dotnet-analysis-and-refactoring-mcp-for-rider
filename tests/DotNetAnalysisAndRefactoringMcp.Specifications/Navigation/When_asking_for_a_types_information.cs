using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

public class When_asking_for_a_types_information(TheSandboxWithTheFixtureSolution sandbox)
{
  readonly string _answer = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "Circle" });

  [XF] public void it_gives_its_namespace() => _answer.Must().Contain("namespace: Shapes");

  [XF(Skip = "Known bug, in dev_docs/backlog.md: the header of a sealed class says 'static class'.")]
  public void it_does_not_call_a_sealed_class_static() => _answer.Must().NotContain("static");
}
