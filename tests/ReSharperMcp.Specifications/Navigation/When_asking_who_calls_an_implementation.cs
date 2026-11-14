using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace ReSharperMcp.Specifications.Navigation;

public class When_asking_who_calls_an_implementation(TheSandboxWithTheFixtureSolution sandbox)
{
  readonly string _answer = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Circle.Area", direction = "incoming" });

  [XF(Skip = "Known bug, in dev_docs/backlog.md: callers that call the implementation through its interface are not found.")]
  public void it_finds_the_callers_that_call_it_through_the_interface() => _answer.Must().Contain("AreaOfTheFirst");
}
