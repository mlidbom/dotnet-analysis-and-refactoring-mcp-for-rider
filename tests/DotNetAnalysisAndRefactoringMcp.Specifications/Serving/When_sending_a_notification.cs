using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

/// <summary>A JSON-RPC request without an id, such as <c>notifications/initialized</c>, which a client sends after connecting.</summary>
public class When_sending_a_notification(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly (int Status, string Body) _answer = sandbox.McpServer.Notify("notifications/initialized");

   [XF] public void it_is_accepted_with_http_202() => _answer.Status.Must().Be(202);

   [XF] public void it_gets_no_answer() => _answer.Body.Must().Be("");
}
