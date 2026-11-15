using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   A solution of another Rider, a peer, registered with this one's MCP server, the primary: the primary forwards the
///   calls for it to the peer's port.
/// </summary>
/// <remarks>The tools as the JSON the peer sent: no type of a tools build, which the host would keep loaded.</remarks>
public sealed class PeerRegistration(string solutionName, string solutionPath, int port, JArray tools)
{
   public string SolutionName { get; } = solutionName;

   /// <summary>The solution file's full path, which tells it from the other solutions.</summary>
   public string SolutionPath { get; } = solutionPath;

   /// <summary>The port the peer's server listens on.</summary>
   public int Port { get; } = port;

   /// <summary>The tools the peer serves the solution with, as their definitions in <c>tools/list</c>.</summary>
   public JArray Tools { get; } = tools;
}
