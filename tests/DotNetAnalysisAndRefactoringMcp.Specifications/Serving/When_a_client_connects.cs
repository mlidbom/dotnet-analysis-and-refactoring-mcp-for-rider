using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public class When_a_client_connects(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly JsonElement _result = sandbox.McpServer.Initialize();

   [XF] public void it_answers_in_the_protocol_version_the_client_asked_for() => _result.GetProperty("protocolVersion").GetString().Must().Be("2025-03-26");

   [XF] public void it_says_it_tells_of_tool_list_changes() =>
      _result.GetProperty("capabilities").GetProperty("tools").GetProperty("listChanged").GetBoolean().Must().Be(true);

   /// <summary>From the tools build since 2026-11-14, so that changing it needs no Rider restart.</summary>
   [XF] public void it_sends_the_usage_guide() => _result.GetProperty("instructions").GetString().Must().Contain("ReSharper code intelligence");
}
