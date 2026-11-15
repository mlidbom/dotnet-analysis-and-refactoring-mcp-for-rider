using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public class When_calling_a_tool_that_does_not_exist(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _refusal = sandbox.McpServer.Refusal("no_such_tool", new {});

   [XF] public void it_is_refused_as_an_unknown_tool() => _refusal.Must().Contain("Unknown tool: no_such_tool");
}
