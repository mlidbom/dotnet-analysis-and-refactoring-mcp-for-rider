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

   /// <summary>
   ///   Codex shows a model only the first 250 characters of a server's instructions until it discovers the server's
   ///   tools (Codex 0.171.0), so the first line, which says what the server is for, must fit in them.
   /// </summary>
   [XF] public void the_usage_guides_first_line_fits_in_the_250_characters_codex_shows_before_tool_discovery() =>
      _result.GetProperty("instructions").GetString()!.Split('\n')[0].Must().Satisfy(it => it.Length < 250);
}
