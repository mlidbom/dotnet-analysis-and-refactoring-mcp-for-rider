using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

/// <summary>
///   Claude Code defers an MCP server's tools until the agent searches for them, unless a tool's <c>_meta</c> says
///   <c>"anthropic/alwaysLoad": true</c>.
/// </summary>
public class When_listing_which_tools_Claude_Code_loads_up_front
{
   readonly string[] _loadedUpFront;

   public When_listing_which_tools_Claude_Code_loads_up_front(TheSandboxWithTheFixtureSolution sandbox) =>
      _loadedUpFront = sandbox.McpServer.ListTools()
                              .Where(it => it.TryGetProperty("_meta", out var meta)
                                        && meta.TryGetProperty("anthropic/alwaysLoad", out var alwaysLoad) && alwaysLoad.ValueKind == JsonValueKind.True)
                              .Select(it => it.GetProperty("name").GetString()!).Order(StringComparer.Ordinal).ToArray();

   [XF] public void they_are_the_navigation_tools_that_replace_text_searches_for_symbols_and_whole_file_reads() =>
      _loadedUpFront.Must().SequenceEqual(["find_usages", "get_symbol_source", "go_to_definition", "list_symbols_in_file"]);
}
