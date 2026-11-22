using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using DotNetAnalysisAndRefactoringMcp.Protocol;
using DotNetAnalysisAndRefactoringMcp.Tools;
using JetBrains.Util;
using JetBrains.Util.Logging;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>A solution open in a peer, another Rider, which registered it with this one: its calls are forwarded to the peer.</summary>
sealed class PeerSolution(IMcpServer server, PeerRegistration peer) : OpenSolution(peer.SolutionName, peer.SolutionPath)
{
   /// <summary>How long a forwarded call may take: a little longer than the peer gives a tool call itself.</summary>
   static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(ToolCall.TimeoutSeconds + 10);

   /// <summary>How long the peer may take to say how it is, for <c>/health</c>, which answers at once.</summary>
   static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(1);

   static readonly ILogger Log = Logger.GetLogger<PeerSolution>();

   readonly IMcpServer _server = server;
   readonly PeerRegistration _registration = peer;
   readonly OtherMcpServer _peersServer = new(peer.Port);

   public override IReadOnlyList<ToolDefinition> Tools =>
      _registration.Tools
                   .Select(tool => new ToolDefinition
                                   {
                                      Name = tool["name"]?.ToString(),
                                      Description = tool["description"]?.ToString(),
                                      Annotations = tool["annotations"],
                                      InputSchema = tool["inputSchema"]
                                   })
                   .ToList();

   public override JsonRpcResponse Call(JsonRpcRequest request, string tool, JObject arguments)
   {
      var forwarded = new JsonRpcRequest
                      {
                         Id = request.Id,
                         Method = "tools/call",
                         Params = new JObject { ["name"] = tool, ["arguments"] = arguments }
                      };
      try
      {
         return JsonRpcResponse.Parse(_peersServer.Send(forwarded, CallTimeout));
      }
      catch(WebException exception) when(ConnectionWasRefused(exception))
      {
         // Nothing listens on the peer's port: its Rider has gone. Until 2026-11-15 any WebException removed the peer,
         // also a slow call's timeout or an HTTP error from a peer that was running.
         Log.Warn($"Removing unreachable peer on port {_registration.Port}");
         _server.RemovePeersOn(_registration.Port);
         return request.ToolError($"Solution is no longer available (peer on port {_registration.Port} is unreachable: {exception.Message})");
      }
      catch(Exception exception)
      {
         return request.ToolError(ToolFailure.Logged(exception, $"the call proxied to the peer on port {_registration.Port}"));
      }

      static bool ConnectionWasRefused(WebException exception)
      {
         if(exception.Status == WebExceptionStatus.ConnectFailure) return true;
         // On CoreCLR, which Rider's backend runs on, a refused connection is UnknownError, with the SocketException inside.
         return exception.Status == WebExceptionStatus.UnknownError
             && exception.GetBaseException() is SocketException { SocketErrorCode: SocketError.ConnectionRefused };
      }
   }

   /// <summary>What the peer says of the solution; or that the peer is unreachable, when it doesn't answer at once.</summary>
   public override JObject Health()
   {
      var unreachable = NameAndPath();
      unreachable["reachable"] = false;
      try
      {
         var status = JObject.Parse(_peersServer.Send(new JsonRpcRequest { Id = 0, Method = "internal/status", Params = new JObject() }, StatusTimeout));
         return status["result"]?["solutions"]?
               .OfType<JObject>()
               .FirstOrDefault(it => it["path"]?.ToString() == FilePath) ?? unreachable;
      }
      catch(Exception exception)
      {
         // Whatever the failure, the answer reports the peer as unreachable: that's the fallback.
         Log.Verbose($"The peer on port {_registration.Port} didn't answer: {exception.Message}");
         return unreachable;
      }
   }
}
