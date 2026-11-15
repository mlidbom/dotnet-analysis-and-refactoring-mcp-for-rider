using DotNetAnalysisAndRefactoringMcp.Protocol;
using DotNetAnalysisAndRefactoringMcp.Tools;
using JetBrains.Util;
using JetBrains.Util.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>
///   One JSON-RPC request to this Rider's MCP server, and its answer: the MCP methods a client calls
///   (<c>initialize</c>, <c>tools/list</c>, <c>tools/call</c>), and the internal ones the Riders' servers call each
///   other with (<c>internal/register</c>, <c>internal/deregister</c>, <c>internal/status</c>, <c>internal/restart</c>).
/// </summary>
sealed class McpRequest(IMcpServer server, JsonRpcRequest request)
{
   static readonly ILogger Log = Logger.GetLogger<McpRequest>();

   readonly OpenSolutions _solutions = OpenSolutions.Of(server);
   readonly JsonRpcRequest _request = request;
   readonly IMcpServer _server = server;

   /// <summary>The answer to the request <paramref name="body"/> is the text of: the response's JSON, or <c>null</c> for a notification.</summary>
   public static string Answer(IMcpServer server, string body)
   {
      Log.Verbose($"MCP request: {body}");
      var request = JsonRpcRequest.Parse(body);
      var response = new McpRequest(server, request).Answer();
      if(request.IsNotification)
         return null;

      var json = response.Json();
      Log.Verbose($"MCP response: {json}");
      return json;
   }

   JsonRpcResponse Answer() =>
      _request.Method switch
      {
         "initialize"                => Initialize(),
         "notifications/initialized" => _request.Answer(new JObject()),
         "tools/list"                => _request.Answer(new ToolsListResult { Tools = ToolList.Of(_solutions) }),
         "tools/call"                => CallTool(),
         "internal/register"         => RegisterPeer(),
         "internal/deregister"       => DeregisterPeer(),
         "internal/status"           => Status(),
         "internal/restart"          => Restart(),
         _                           => _request.Error(-32601, $"Method not found: {_request.Method}")
      };

   /// <summary>The handshake: the protocol version, older clients' included, and the usage guide.</summary>
   JsonRpcResponse Initialize()
   {
      const string olderVersion = "2024-11-05";
      var clientsVersion = _request.Params?["protocolVersion"]?.ToString();

      return _request.Answer(new InitializeResult
                            {
                               ProtocolVersion = clientsVersion == olderVersion ? olderVersion : "2025-03-26",
                               Instructions = McpInstructions.Text
                            });
   }

   /// <summary>A tool's call, routed to the solution it is for, whose own tools answer it; <c>list_solutions</c> is answered here.</summary>
   JsonRpcResponse CallTool()
   {
      var tool = _request.Params?["name"]?.ToString();
      var arguments = _request.Params?["arguments"] as JObject ?? new JObject();
      if(tool == "list_solutions")
         return _request.ToolAnswer(_solutions.Listed().ToString(Formatting.Indented));

      // The solution's tools don't know the parameter: it is the server's, for routing.
      var solutionName = arguments["solutionName"]?.ToString();
      arguments.Remove("solutionName");

      var (chosen, refusal) = _solutions.Choose(solutionName, arguments);
      return chosen?.Call(_request, tool, arguments) ?? _request.ToolError(refusal);
   }

   /// <summary>
   ///   A peer registers a solution when it opens one, and again when it swaps in a new tools build: the clients are
   ///   told that the tool list changed.
   /// </summary>
   JsonRpcResponse RegisterPeer()
   {
      var port = _request.Params?["port"]?.Value<int>() ?? 0;
      var name = _request.Params?["solutionName"]?.ToString();
      var path = _request.Params?["solutionPath"]?.ToString();

      if(port > 0 && !string.IsNullOrEmpty(path))
      {
         _server.RegisterPeer(new PeerRegistration(name, path, port, _request.Params?["tools"] as JArray ?? new JArray()));
         Log.Info($"Registered peer solution '{name}' on port {port}");
         _server.NotifyToolsChanged();
      }

      return Ok();
   }

   JsonRpcResponse DeregisterPeer()
   {
      var path = _request.Params?["solutionPath"]?.ToString();

      if(!string.IsNullOrEmpty(path))
      {
         _server.DeregisterPeer(path);
         Log.Info($"Deregistered peer solution at '{path}'");
      }

      return Ok();
   }

   /// <summary>
   ///   How this server is, which a primary asks its peers for <c>/health</c>, and the watchdog asks to check that the
   ///   listener answers: its port, its role, its solutions' health.
   /// </summary>
   JsonRpcResponse Status()
   {
      var solutions = new JArray();
      foreach(var solution in _solutions.Local)
         solutions.Add(solution.Health());
      foreach(var solution in _solutions.Peers)
         solutions.Add(solution.NameAndPath());

      return _request.Answer(new JObject
                            {
                               ["port"] = _server.Port,
                               ["role"] = _server.IsPrimary ? "primary" : "peer",
                               ["solutions"] = solutions
                            });
   }

   JsonRpcResponse Restart()
   {
      _server.Restart();
      return Ok();
   }

   JsonRpcResponse Ok() => _request.Answer(new JObject { ["ok"] = true });
}
