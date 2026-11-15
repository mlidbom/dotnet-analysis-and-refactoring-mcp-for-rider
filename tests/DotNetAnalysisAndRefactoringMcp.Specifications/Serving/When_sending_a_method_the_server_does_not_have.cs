using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public class When_sending_a_method_the_server_does_not_have(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly JsonElement _error = sandbox.McpServer.Error("no/such/method");

   [XF] public void it_answers_json_rpcs_method_not_found_error() => _error.GetProperty("code").GetInt32().Must().Be(-32601);
}
