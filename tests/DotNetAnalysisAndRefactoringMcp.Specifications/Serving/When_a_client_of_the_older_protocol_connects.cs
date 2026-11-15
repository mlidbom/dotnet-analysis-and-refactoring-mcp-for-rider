using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

/// <summary>A client of MCP's protocol version 2024-11-05, the one before the one the server speaks.</summary>
public class When_a_client_of_the_older_protocol_connects(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly JsonElement _result = sandbox.McpServer.Initialize("2024-11-05");

   [XF] public void it_answers_in_the_older_protocol_version() => _result.GetProperty("protocolVersion").GetString().Must().Be("2024-11-05");
}
