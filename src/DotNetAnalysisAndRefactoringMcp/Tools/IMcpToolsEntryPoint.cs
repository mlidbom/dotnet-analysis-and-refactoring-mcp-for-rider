namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   The way into a build of the tools assembly: the host creates the type <see cref="McpToolsEntryPoint.TypeName"/> names when it loads the build,
///   and hands it every request the MCP server gets.
/// </summary>
/// <remarks>
///   What crosses is the host's types and plain data, never an object of the build: one the host kept would keep the
///   build loaded after it was swapped out. The whole protocol is the build's: answering the JSON-RPC requests, routing
///   calls to the solutions and the peers, a call's flow, the health report. Until 2026-11-15 the host had it, and
///   changing it took a Rider restart.
/// </remarks>
public interface IMcpToolsEntryPoint
{
   /// <summary>
   ///   The answer to a JSON-RPC request that a client, or another Rider's server, posted: the response's JSON, or
   ///   <c>null</c> for a notification, which gets none.
   /// </summary>
   string Answer(IMcpServer server, string request);

   /// <summary>The JSON of <c>GET /health</c>: the open solutions, whether they're indexing, the tools builds.</summary>
   string Health(IMcpServer server);

   /// <summary>Registers <paramref name="solution"/> with the primary, when <paramref name="server"/> is a peer.</summary>
   void RegisterWithThePrimary(IMcpServer server, IMcpSolution solution);

   /// <summary>Tells the primary that <paramref name="solution"/> closed, when <paramref name="server"/> is a peer.</summary>
   void DeregisterFromThePrimary(IMcpServer server, IMcpSolution solution);
}

public static class McpToolsEntryPoint
{
   /// <summary>The type in the tools assembly that implements <see cref="IMcpToolsEntryPoint"/>.</summary>
   public const string TypeName = "DotNetAnalysisAndRefactoringMcp.Tools.ToolsEntryPoint";

   /// <summary>The tools assembly's file name, in every build folder.</summary>
   public const string AssemblyFileName = "DotNetAnalysisAndRefactoringMcp.Tools.dll";
}
