namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   A tool whose definition Claude Code loads into a session at its start, rather than when the agent searches for
///   it: the tools agents should reach for before a text search or a whole-file read.
/// </summary>
/// <remarks>
///   <para>
///     Claude Code defers MCP tools: a session sees only their names, and loads a tool's definition through its tool
///     search before it can call the tool. Agents search for the tools they already meant to use. From 16 to 23
///     November 2026, 29 of the 186 Claude sessions offered Royder ever loaded a navigation tool, and agents looked up
///     about 4,800 symbols by text search, against 117 navigation calls (<c>dev_docs/what-agents-lack.md</c>).
///   </para>
///   <para>
///     Sent in <c>tools/list</c> as <c>_meta: {"anthropic/alwaysLoad": true}</c>. Each such definition is read again
///     by every request of every session whose project names Royder, so the set is kept to the few that replace text
///     lookups.
///   </para>
/// </remarks>
public interface IMcpAlwaysLoadedTool : IMcpTool {}
