namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   The <c>instructions</c> of the MCP handshake: what Royder is for, which clients show the agent at a session's
///   start.
/// </summary>
/// <remarks>
///   Claude Code shows them even while the tools themselves are deferred, as a list of names. Agents looked symbols up
///   by text search about 4,800 times from 16 to 23 November 2026, against 117 calls of Royder's navigation tools
///   (<c>dev_docs/what-agents-lack.md</c>), so they say which tool takes the place of which text lookup. Codex shows the
///   first 250 characters before its tool discovery, so the whole of them fit in that.
/// </remarks>
public static class McpInstructions
{
   /// <summary>What Royder is for, wherever it is served: by the standalone primary, and by a Rider directly.</summary>
   public const string Purpose =
      "Look up C# symbols with go_to_definition, get_symbol_source, find_usages, not text search; "
    + "list_symbols_in_file before reading a big file.";

   /// <summary>The instructions of a Rider's own endpoint, which serves the solutions open in it.</summary>
   public const string Text = Purpose + " Select a solution by file path or solutionName.";
}
