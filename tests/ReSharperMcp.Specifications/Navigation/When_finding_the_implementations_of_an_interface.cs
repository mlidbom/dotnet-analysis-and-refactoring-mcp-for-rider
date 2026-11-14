using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace ReSharperMcp.Specifications.Navigation;

public class When_finding_the_implementations_of_an_interface(TheSandboxWithTheFixtureSolution sandbox)
{
  readonly string _answer = sandbox.McpServer.CallTool("find_implementations", new { symbolName = "IShape" });

  [XF] public void it_finds_every_class_that_implements_it() => _answer.Must().Contain("Circle").Contain("Square");
}
