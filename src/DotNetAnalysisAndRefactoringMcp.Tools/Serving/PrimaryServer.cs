using System;
using System.Linq;
using DotNetAnalysisAndRefactoringMcp.Protocol;
using DotNetAnalysisAndRefactoringMcp.Tools;
using JetBrains.Util;
using JetBrains.Util.Logging;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>
///   The primary MCP server, as a peer reaches it: the peer registers its solutions there, with their tools, so that
///   the primary lists them and forwards their calls. The primary answers in <see cref="McpRequest"/>.
/// </summary>
sealed class PrimaryServer(IMcpServer peer)
{
   static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

   static readonly ILogger Log = Logger.GetLogger<PrimaryServer>();

   readonly OtherMcpServer _primary = new(peer.PrimaryPort);
   readonly IMcpServer _peer = peer;

   /// <summary>Registers <paramref name="solution"/>, or registers it again, with the tools of a new build.</summary>
   public void Register(LocalSolution solution)
   {
      Send("internal/register",
           new JObject
           {
              ["port"] = _peer.Port,
              ["solutionName"] = solution.Name,
              ["solutionPath"] = solution.FilePath,
              ["tools"] = new JArray(solution.Tools.Select(JsonOf.Value))
           });
      Log.Info($"Registered solution '{solution.Name}' with primary server on port {_primary.Port}");
   }

   /// <summary>Tells the primary that <paramref name="solution"/> closed.</summary>
   public void Deregister(IMcpSolution solution)
   {
      Send("internal/deregister", new JObject { ["solutionPath"] = solution.FilePath });
      Log.Info($"Deregistered solution '{solution.Name}' from primary server on port {_primary.Port}");
   }

   void Send(string method, JObject parameters) => _primary.Send(new JsonRpcRequest { Id = 1, Method = method, Params = parameters }, Timeout);
}
