using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   This Rider's MCP server, as the host hands it to a tools build for one request: the solutions registered with it,
///   which outlive the build, and what only the host can do, such as telling the clients that the tool list changed.
/// </summary>
/// <remarks>
///   Each Rider runs a server. The first binds the base port and is the primary, which the clients connect to; the
///   others are its peers, which register their solutions with it, and it forwards the calls for them.
/// </remarks>
public interface IMcpServer
{
   /// <summary>The port it listens on.</summary>
   int Port { get; }

   /// <summary>The primary's port: the base port, which the clients connect to.</summary>
   int PrimaryPort { get; }

   /// <summary>Whether it is the primary, listening on <see cref="PrimaryPort"/>.</summary>
   bool IsPrimary { get; }

   /// <summary>The solutions open in this Rider, as they were when the request came.</summary>
   IReadOnlyList<IMcpSolution> Solutions { get; }

   /// <summary>The solutions the peers registered with it, as they were when the request came.</summary>
   IReadOnlyList<PeerRegistration> Peers { get; }

   /// <summary>What the tools builds' swapping reports: the build served, the ones swapped out, one that failed to load.</summary>
   JObject ToolsBuilds { get; }

   /// <summary>Registers a peer's solution, or registers it again, with the tools of a new build.</summary>
   void RegisterPeer(PeerRegistration peer);

   /// <summary>Forgets the peer's solution at <paramref name="solutionPath"/>, which its Rider closed.</summary>
   void DeregisterPeer(string solutionPath);

   /// <summary>Forgets the solutions of the peer on <paramref name="port"/>, whose Rider has gone.</summary>
   void RemovePeersOn(int port);

   /// <summary>Tells the clients with an event stream open that the tool list changed, so they list the tools again.</summary>
   void NotifyToolsChanged();

   /// <summary>Restarts the HTTP listener, on a thread of the pool.</summary>
   void Restart();
}
