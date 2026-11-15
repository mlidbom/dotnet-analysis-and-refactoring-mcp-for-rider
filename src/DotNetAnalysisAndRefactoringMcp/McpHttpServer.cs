using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using JetBrains.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DotNetAnalysisAndRefactoringMcp.Protocol;
using DotNetAnalysisAndRefactoringMcp.Threading;
using DotNetAnalysisAndRefactoringMcp.Tools;

namespace DotNetAnalysisAndRefactoringMcp;

public class McpHttpServer
{
   volatile HttpListener _listener;
   readonly ILogger _logger;
   readonly IThreadShared<Registrations> _registrations = IThreadShared.New(new Registrations());
   readonly string _sessionId;
   readonly Func<string> _instructions;
   readonly IThreadShared<List<ServerEventStream>> _eventStreams = IThreadShared.New(new List<ServerEventStream>());
   Thread _listenerThread;
   volatile bool _running;

   public int Port { get; }
   public bool IsPrimary { get; set; }

   /// <param name="instructions">The usage guide for the <c>initialize</c> response, from the tools build served.</param>
   public McpHttpServer(int port, ILogger logger, Func<string> instructions)
   {
      Port = port;
      _logger = logger;
      _instructions = instructions;
      _sessionId = Guid.NewGuid().ToString("N");
      _listener = new HttpListener();
      _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
   }

   /// <param name="listTools">The tools of the tools build currently served.</param>
   /// <param name="callTool">The answer of a tool of the build currently served, as text, or null if it has none by that name.</param>
   public void RegisterSolution(string solutionName,
                                string solutionPath,
                                Func<List<ToolDefinition>> listTools,
                                Func<string, JObject, string> callTool,
                                Func<JObject> health)
   {
      _registrations.Locked(it => it.Solutions[solutionPath] = new SolutionRegistration
                                                               {
                                                                  Name = solutionName,
                                                                  Path = solutionPath,
                                                                  ListTools = listTools,
                                                                  CallTool = callTool,
                                                                  Health = health
                                                               });
   }

   public void UnregisterSolution(string solutionPath)
   {
      _registrations.Locked(it => it.Solutions.Remove(solutionPath));
   }

   /// <summary>The solutions of this process.</summary>
   internal List<SolutionRegistration> LocalSolutions() => Registered().Local;

   /// <summary>The solutions registered now, this process's and the peers'.</summary>
   (List<SolutionRegistration> Local, List<PeerRegistration> Peers) Registered() =>
      _registrations.Locked(it => (it.Solutions.Values.ToList(), it.Peers.Values.ToList()));

   /// <summary>
   /// Tells every client with an event stream open (a GET) that the tool list changed, so that it lists the tools
   /// again: <c>notifications/tools/list_changed</c>. Without it a tool a new tools build adds appears only when
   /// the client reconnects.
   /// </summary>
   public void NotifyToolsChanged()
   {
      var streams = _eventStreams.Locked(it => it.ToList());

      const string message = "event: message\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n";
      var told = streams.Count(stream => stream.TrySend(message));
      _logger.Info($"Told {told} of {streams.Count} MCP clients with an event stream open that the tool list changed");
   }

   /// <summary>
   /// Copies all local solution registrations to another server instance.
   /// Used when a peer promotes itself to primary.
   /// </summary>
   public void TransferRegistrationsTo(McpHttpServer target)
   {
      foreach(var s in Registered().Local)
         target.RegisterSolution(s.Name, s.Path, s.ListTools, s.CallTool, s.Health);
   }

   public void Start()
   {
      _running = true;
      _listener.Start();
      _listenerThread = new Thread(ListenLoop)
                        {
                           IsBackground = true,
                           Name = "DotNetAnalysisAndRefactoringMcp-HttpListener"
                        };
      _listenerThread.Start();
      _logger.Info($"MCP server listening on http://127.0.0.1:{Port}/");
   }

   /// <remarks>
   ///   <c>Close</c>, not <c>Stop</c>: a listener whose <c>Start</c> failed, as when probing for a free port, is disposed
   ///   already, and <c>Stop</c> then throws <c>ObjectDisposedException</c>. <c>Close</c> threw in no state measured on
   ///   .NET 10 (2026-11-15): after a failed start, twice over, after a stop.
   /// </remarks>
   public void Stop()
   {
      _running = false;
      _listener.Close();
   }

   public void Restart()
   {
      ThreadPool.QueueUserWorkItem(_ =>
      {
         try
         {
            _logger.Info("Restarting MCP HTTP listener...");

            // Signal the listen loop to stop and tear down the old listener (Close: see Stop)
            _running = false;
            _listener.Close();

            // Wait for the old listener thread to actually exit (avoids two threads
            // calling GetContext on the same listener after _running goes back to true)
            var oldThread = _listenerThread;
            if(oldThread != null && oldThread.IsAlive)
               oldThread.Join(TimeSpan.FromSeconds(5));

            var newListener = new HttpListener();
            newListener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            newListener.Start();
            _listener = newListener;
            _running = true;

            _listenerThread = new Thread(ListenLoop)
                              {
                                 IsBackground = true,
                                 Name = "DotNetAnalysisAndRefactoringMcp-HttpListener"
                              };
            _listenerThread.Start();

            _logger.Info($"MCP HTTP listener restarted on port {Port}");
         }
         catch(Exception ex)
         {
            // A thread-pool thread: anything that escapes it ends Rider's backend. The watchdog's next ping retries.
            ToolFailure.Logged(ex, "restarting the MCP HTTP listener");
         }
      });
   }

   void ListenLoop()
   {
      int consecutiveErrors = 0;

      while(_running)
      {
         try
         {
            var context = _listener.GetContext();
            consecutiveErrors = 0;
            ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
         }
         catch(HttpListenerException) when(!_running)
         {
            break;
         }
         catch(ObjectDisposedException) when(!_running)
         {
            break;
         }
         catch(Exception ex)
         {
            if(!_running) break;

            consecutiveErrors++;
            ToolFailure.Logged(ex, $"accepting an HTTP connection ({consecutiveErrors} failures in a row)");

            if(consecutiveErrors >= 3)
            {
               _logger.Warn("HttpListener appears broken — attempting in-place recovery");
               if(TryRecoverListener())
               {
                  consecutiveErrors = 0;
               } else
               {
                  // Back off before retrying to avoid tight spin
                  Thread.Sleep(5000);
               }
            } else
            {
               // Brief pause to avoid tight spin on transient errors
               Thread.Sleep(200);
            }
         }
      }
   }

   /// <summary>
   /// Attempts to recreate the HttpListener in-place when it enters an unrecoverable state.
   /// Called from the listen loop after consecutive failures.
   /// </summary>
   bool TryRecoverListener()
   {
      try
      {
         _listener.Close(); // see Stop
         Thread.Sleep(500);

         var newListener = new HttpListener();
         newListener.Prefixes.Add($"http://127.0.0.1:{Port}/");
         newListener.Start();
         _listener = newListener;

         _logger.Info($"HttpListener recovered on port {Port}");
         return true;
      }
      catch(Exception ex)
      {
         ToolFailure.Logged(ex, "recovering the HttpListener");
         return false;
      }
   }

   void HandleRequest(HttpListenerContext context)
   {
      try
      {
         // CORS headers — support GET, POST, DELETE for Streamable HTTP transport
         context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
         context.Response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
         context.Response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept, Mcp-Session-Id");
         context.Response.Headers.Add("Access-Control-Expose-Headers", "Mcp-Session-Id");

         if(context.Request.HttpMethod == "OPTIONS")
         {
            context.Response.StatusCode = 204;
            context.Response.Close();
            return;
         }

         // Validate session ID if the client provides one — reject mismatches with 404 per spec
         var clientSessionId = context.Request.Headers["Mcp-Session-Id"];
         if(clientSessionId != null && clientSessionId != _sessionId)
         {
            context.Response.StatusCode = 404;
            context.Response.Close();
            return;
         }

         // Attach session ID to all non-OPTIONS responses
         context.Response.Headers.Add("Mcp-Session-Id", _sessionId);

         switch(context.Request.HttpMethod)
         {
            case "POST":
               HandlePost(context);
               break;
            case "GET" when context.Request.Url.AbsolutePath.TrimEnd('/') == "/health":
               HandleHealth(context);
               break;
            case "GET":
               HandleGetSse(context);
               break;
            case "DELETE":
               HandleDeleteSession(context);
               break;
            default:
               context.Response.StatusCode = 405;
               context.Response.Headers.Add("Allow", "GET, POST, DELETE, OPTIONS");
               context.Response.Close();
               break;
         }
      }
      catch(Exception ex)
      {
         // A thread-pool thread: anything that escapes it ends Rider's backend. The client gets a 500.
         ToolFailure.Logged(ex, "an MCP request");
         try
         {
            context.Response.StatusCode = 500;
            context.Response.Close();
         }
         catch(Exception answering)
         {
            // A thread-pool thread: anything that escapes it ends Rider's backend. The failure is logged above, and
            // answering it fails when the client has gone or the answer was begun already.
            _logger.Verbose($"Couldn't answer 500 after '{ex.Message}': {answering.Message}");
         }
      }
   }

   void HandlePost(HttpListenerContext context)
   {
      string body;
      using(var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
      {
         body = reader.ReadToEnd();
      }

      _logger.Verbose($"MCP request: {body}");

      var request = JsonConvert.DeserializeObject<JsonRpcRequest>(body);

      // JSON-RPC notifications have no id — respond with 202 Accepted, no body
      if(request.Id == null && request.Method != null)
      {
         ProcessRequest(request); // still process for side effects
         context.Response.StatusCode = 202;
         context.Response.Close();
         return;
      }

      var response = ProcessRequest(request);
      var responseJson = JsonConvert.SerializeObject(response,
                                                     Formatting.None,
                                                     new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

      _logger.Verbose($"MCP response: {responseJson}");

      var responseBytes = Encoding.UTF8.GetBytes(responseJson);
      context.Response.ContentType = "application/json";
      context.Response.ContentLength64 = responseBytes.Length;
      context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
      context.Response.Close();
   }

   /// <summary>
   /// Streamable HTTP: GET opens an SSE stream for server-to-client notifications, which
   /// <see cref="NotifyToolsChanged"/> writes to. Kept alive with a comment every 15 seconds.
   /// </summary>
   void HandleGetSse(HttpListenerContext context)
   {
      context.Response.ContentType = "text/event-stream";
      context.Response.Headers.Add("Cache-Control", "no-cache");
      context.Response.StatusCode = 200;

      ServerEventStream stream = null;
      try
      {
         using(var writer = new StreamWriter(context.Response.OutputStream, new UTF8Encoding(false)))
         {
            writer.AutoFlush = true;
            stream = new ServerEventStream(writer);

            // Initial SSE comment to confirm the connection is established
            if(!stream.TrySend(": connected\n\n"))
               return;

            _eventStreams.Locked(it => it.Add(stream));

            // Keep-alive loop until server stops or client disconnects
            while(_running)
            {
               Thread.Sleep(15000);
               if(!stream.TrySend(": keepalive\n\n"))
                  break; // Client disconnected
            }
         }
      }
      catch(Exception exception)
      {
         // A thread-pool thread: anything that escapes it ends Rider's backend. A client that goes away ends its
         // stream, and nothing waits for an answer: the log is where the failure is told.
         _logger.Verbose($"An MCP event stream ended: {exception.Message}");
      }
      finally
      {
         if(stream != null)
            _eventStreams.Locked(it => it.Remove(stream));
         try
         {
            context.Response.Close();
         }
         catch(Exception closing)
         {
            // As above: on a thread-pool thread, and the client may have gone.
            _logger.Verbose($"Closing an MCP event stream's response failed: {closing.Message}");
         }
      }
   }

   /// <summary>
   /// Streamable HTTP: DELETE terminates the session.
   /// We don't track per-session state, so just acknowledge.
   /// </summary>
   void HandleDeleteSession(HttpListenerContext context)
   {
      context.Response.StatusCode = 200;
      context.Response.Close();
   }

   JsonRpcResponse ProcessRequest(JsonRpcRequest request)
   {
      switch(request.Method)
      {
         case "initialize":
            // Negotiate protocol version — accept older clients for backwards compatibility
            var clientVersion = request.Params?["protocolVersion"]?.ToString();
            var negotiatedVersion = "2025-03-26";
            if(clientVersion == "2024-11-05")
               negotiatedVersion = "2024-11-05";
            return new JsonRpcResponse
                   {
                      Id = request.Id,
                      Result = new InitializeResult
                               {
                                  ProtocolVersion = negotiatedVersion,
                                  Instructions = InstructionsOrNone()
                               }
                   };

         case "notifications/initialized":
            return new JsonRpcResponse
                   {
                      Id = request.Id,
                      Result = new JObject()
                   };

         case "tools/list":
            return HandleToolsList(request);

         case "tools/call":
            return HandleToolCall(request);

         case "internal/register":
            return HandlePeerRegister(request);

         case "internal/deregister":
            return HandlePeerDeregister(request);

         case "internal/status":
            return HandleInternalStatus(request);

         case "internal/restart":
            return HandleInternalRestart(request);

         default:
            return new JsonRpcResponse
                   {
                      Id = request.Id,
                      Error = new JsonRpcError
                              {
                                 Code = -32601,
                                 Message = $"Method not found: {request.Method}"
                              }
                   };
      }
   }

   /// <summary>The usage guide of the tools build served; none when there is no build to serve.</summary>
   string InstructionsOrNone()
   {
      try
      {
         return _instructions();
      }
      catch(Exception exception)
      {
         // Initialize still answers: without a tools build to serve, or one that fails, there's no guide to send.
         ToolFailure.Logged(exception, "the usage guide for the initialize response");
         return null;
      }
   }

   JsonRpcResponse HandleToolsList(JsonRpcRequest request)
   {
      var tools = new List<ToolDefinition>();
      var (local, peers) = Registered();

      // Outside the lock: listing may load a new tools build first.
      // Collect unique tools across all local solutions (they serve the same build)
      var seen = new HashSet<string>();
      foreach(var solution in local)
      {
         foreach(var tool in solution.ListTools())
         {
            if(seen.Add(tool.Name))
               tools.Add(tool);
         }
      }

      // If no local solutions but we have peers, use peer tool info
      if(tools.Count == 0 && peers.Count > 0)
      {
         foreach(var peer in peers)
         {
            foreach(var tool in peer.Tools)
            {
               if(seen.Add(tool.Name))
                  tools.Add(tool);
            }
         }
      }

      // Add solutionName as an optional parameter to each tool's schema
      var enriched = tools.Select(AddSolutionNameParam).ToList();

      // Prepend the list_solutions meta-tool
      enriched.Insert(0,
                      new ToolDefinition
                      {
                         Name = "list_solutions",
                         Description =
                            "List all currently open solutions in Rider. " +
                            "Use this to discover available solution names when multiple solutions are open.",
                         InputSchema = new
                                       {
                                          type = "object",
                                          properties = new {},
                                          required = new string[0]
                                       }
                      });

      return new JsonRpcResponse
             {
                Id = request.Id,
                Result = new ToolsListResult { Tools = enriched }
             };
   }

   static ToolDefinition AddSolutionNameParam(ToolDefinition original)
   {
      var schema = (JObject)JsonOf.Value(original.InputSchema);
      var props = schema["properties"] as JObject ?? new JObject();
      props["solutionName"] = JObject.FromObject(new
                                                 {
                                                    type = "string",
                                                    description =
                                                       "Target solution name (e.g. 'MyProject'), a unique path segment (e.g. 'my-repo'), or full path. " +
                                                       "Optional when only one solution is open. " +
                                                       "Required when multiple solutions are open — use list_solutions to see available names and uniquePathSegment hints."
                                                 });
      schema["properties"] = props;

      return new ToolDefinition
             {
                Name = original.Name,
                Description = original.Description,
                InputSchema = schema
             };
   }

   JsonRpcResponse HandleToolCall(JsonRpcRequest request)
   {
      var toolName = request.Params?["name"]?.ToString();
      var arguments = request.Params?["arguments"] as JObject ?? new JObject();

      if(toolName == "list_solutions")
         return HandleListSolutions(request);

      // Extract and remove solutionName before passing to the tool handler
      var solutionName = arguments["solutionName"]?.ToString();
      arguments.Remove("solutionName");

      // Resolve the target solution from the solutions registered now, then call it
      Func<string, JObject, string> localCall = null;
      int peerPort = 0;

      var (local, peers) = Registered();
      var all = new List<SolutionTarget>();

      foreach(var s in local)
         all.Add(new SolutionTarget { Name = s.Name, Path = s.Path, IsLocal = true, CallTool = s.CallTool });

      foreach(var p in peers)
         all.Add(new SolutionTarget { Name = p.SolutionName, Path = p.SolutionPath, IsLocal = false, PeerPort = p.Port });

      if(all.Count == 0)
         return ToolError(request, "No solutions are currently open in Rider.");

      SolutionTarget target;

      if(solutionName != null)
      {
         // 1. Try exact name or path match
         var matches = all
                      .Where(s => s.Name.Equals(solutionName, StringComparison.OrdinalIgnoreCase)
                               || s.Path.Equals(solutionName, StringComparison.OrdinalIgnoreCase))
                      .ToList();

         // 2. If ambiguous or no match, try path-segment matching
         if(matches.Count != 1)
         {
            var segmentMatches = all
                                .Where(s => PathContainsSegment(s.Path, solutionName))
                                .ToList();

            if(segmentMatches.Count == 1)
            {
               // Path-segment uniquely identifies a solution
               matches = segmentMatches;
            } else if(matches.Count == 0 && segmentMatches.Count > 0)
            {
               // No exact matches — use segment matches (may still be ambiguous)
               matches = segmentMatches;
            }
         }

         if(matches.Count == 0)
         {
            var available = string.Join(", ", all.Select(s => $"'{s.Name}'"));
            return ToolError(request,
                             $"Solution '{solutionName}' not found. Available solutions: {available}");
         }

         if(matches.Count > 1)
         {
            var disambiguators = ComputeDisambiguators(
               all.Select(s => new NameAndPath { Name = s.Name, Path = s.Path }).ToList());
            var available = string.Join("\n",
                                        matches.Select(s =>
                                        {
                                           var hint = disambiguators.TryGetValue(s.Path, out var h) ? h : null;
                                           var hintText = hint != null ? $" — use solutionName: \"{hint}\"" : "";
                                           return $"  - {s.Name} ({s.Path}){hintText}";
                                        }));
            return ToolError(request,
                             $"Ambiguous solution name '{solutionName}'. Matches:\n{available}");
         }

         target = matches[0];
      } else if(all.Count == 1)
      {
         target = all[0];
      } else if(FileArguments.Of(arguments) is { Count: > 0 } paths)
      {
         // Pick the solution whose work tree holds the named files. For absolute paths the deepest tree wins
         // (nested checkouts); a relative path found in several checkouts names none of them.
         var holders = all
                      .Where(s => paths.All(p => Holds(s.Path, p)))
                      .OrderByDescending(s => SolutionDirectory(s.Path).Length)
                      .ToList();

         if(holders.Count > 1 && !paths.All(Path.IsPathRooted))
         {
            var names = string.Join(", ", holders.Select(s => $"{s.Name} ({SolutionDirectory(s.Path)})"));
            return ToolError(request,
                             $"The relative path exists in several open solutions: {names}. Pass 'solutionName' or an absolute path.");
         }

         target = holders.FirstOrDefault();

         if(target == null)
         {
            var open = string.Join(", ", all.Select(s => $"{s.Name} ({SolutionDirectory(s.Path)})"));
            return ToolError(request,
                             $"Path is outside open solutions: {open}. Pass 'solutionName' to pick one anyway.");
         }
      } else
      {
         var available = string.Join("\n",
                                     all.Select(s => $"  - {s.Name} ({s.Path})"));
         return ToolError(request,
                          "Multiple solutions are open. Specify 'solutionName' in the arguments, "
                        + "or pass a filePath that exists in only one of them.\n"
                        + $"Available solutions:\n{available}");
      }

      if(target.IsLocal)
      {
         localCall = target.CallTool;
      } else
      {
         peerPort = target.PeerPort;
      }

      if(peerPort > 0)
         return ProxyToPeer(request, peerPort, toolName, arguments);

      try
      {
         var text = localCall(toolName, arguments);
         if(text == null)
            return ToolError(request, $"Unknown tool: {toolName}");

         return new JsonRpcResponse
                {
                   Id = request.Id,
                   Result = new CallToolResult
                            {
                               Content = { new ContentBlock { Text = text } }
                            }
                };
      }
      catch(Exception ex)
      {
         // Warn, not Error: errors surface as IDE exception balloons, and a failing tool call is the caller's news.
         // As text: an exception kept keeps loaded every tools build its stack trace runs through.
         _logger.Warn($"DotNetAnalysisAndRefactoringMcp tool '{toolName}' failed: {ex}");
         return ToolError(request, ToolFailure.Line(ex, toolName));
      }
   }

   JsonRpcResponse ProxyToPeer(JsonRpcRequest originalRequest, int peerPort, string toolName, JObject arguments)
   {
      try
      {
         var peerRequest = new JsonRpcRequest
                           {
                              Id = originalRequest.Id,
                              Method = "tools/call",
                              Params = new JObject
                                       {
                                          ["name"] = toolName,
                                          ["arguments"] = arguments
                                       }
                           };

         var json = JsonConvert.SerializeObject(peerRequest);
         var url = $"http://127.0.0.1:{peerPort}/";

         var webRequest = (HttpWebRequest)WebRequest.Create(url);
         webRequest.Method = "POST";
         webRequest.ContentType = "application/json";
         webRequest.Timeout = 130000; // slightly more than tool timeout

         var bytes = Encoding.UTF8.GetBytes(json);
         webRequest.ContentLength = bytes.Length;
         using(var stream = webRequest.GetRequestStream())
            stream.Write(bytes, 0, bytes.Length);

         using(var response = (HttpWebResponse)webRequest.GetResponse())
         using(var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
         {
            var responseJson = reader.ReadToEnd();
            return JsonConvert.DeserializeObject<JsonRpcResponse>(responseJson);
         }
      }
      catch(WebException ex) when(ex.Status == WebExceptionStatus.ConnectFailure)
      {
         // Nothing listens on the peer's port: its Rider has gone. Until 2026-11-15 any WebException removed the peer,
         // also a slow call's timeout or an HTTP error from a peer that was running.
         _registrations.Locked(it =>
         {
            var staleKey = it.Peers.FirstOrDefault(p => p.Value.Port == peerPort).Key;
            if(staleKey != null)
            {
               _logger.Warn($"Removing unreachable peer on port {peerPort}");
               it.Peers.Remove(staleKey);
            }
         });

         return ToolError(originalRequest,
                          $"Solution is no longer available (peer on port {peerPort} is unreachable: {ex.Message})");
      }
      catch(Exception ex)
      {
         return ToolError(originalRequest, ToolFailure.Logged(ex, $"the call proxied to the peer on port {peerPort}"));
      }
   }

   JsonRpcResponse HandleListSolutions(JsonRpcRequest request)
   {
      var solutionObjects = new JArray();

      var (local, peers) = Registered();
      var allEntries = new List<NameAndPath>();

      foreach(var s in local)
         allEntries.Add(new NameAndPath { Name = s.Name, Path = s.Path });
      foreach(var p in peers)
         allEntries.Add(new NameAndPath { Name = p.SolutionName, Path = p.SolutionPath });

      var disambiguators = ComputeDisambiguators(allEntries);

      foreach(var s in local)
      {
         var obj = new JObject
                   {
                      ["name"] = s.Name,
                      ["path"] = s.Path,
                      ["toolCount"] = s.ListTools().Count
                   };
         if(disambiguators.TryGetValue(s.Path, out var hint))
            obj["uniquePathSegment"] = hint;
         solutionObjects.Add(obj);
      }

      foreach(var p in peers)
      {
         var obj = new JObject
                   {
                      ["name"] = p.SolutionName,
                      ["path"] = p.SolutionPath,
                      ["toolCount"] = p.Tools.Count
                   };
         if(disambiguators.TryGetValue(p.SolutionPath, out var hint))
            obj["uniquePathSegment"] = hint;
         solutionObjects.Add(obj);
      }

      var result = new JObject
                   {
                      ["solutionCount"] = solutionObjects.Count,
                      ["solutions"] = solutionObjects
                   };

      return new JsonRpcResponse
             {
                Id = request.Id,
                Result = new CallToolResult
                         {
                            Content = { new ContentBlock { Text = result.ToString(Formatting.Indented) } }
                         }
             };
   }

   #region Peer registration (internal protocol)

   JsonRpcResponse HandlePeerRegister(JsonRpcRequest request)
   {
      var port = request.Params?["port"]?.Value<int>() ?? 0;
      var name = request.Params?["solutionName"]?.ToString();
      var path = request.Params?["solutionPath"]?.ToString();
      var toolsToken = request.Params?["tools"] as JArray;

      if(port > 0 && !string.IsNullOrEmpty(path))
      {
         var tools = new List<ToolDefinition>();
         if(toolsToken != null)
         {
            foreach(var t in toolsToken)
            {
               tools.Add(new ToolDefinition
                         {
                            Name = t["name"]?.ToString(),
                            Description = t["description"]?.ToString(),
                            InputSchema = t["inputSchema"]
                         });
            }
         }

         _registrations.Locked(it => it.Peers[path] = new PeerRegistration
                                                      {
                                                         SolutionName = name,
                                                         SolutionPath = path,
                                                         Port = port,
                                                         Tools = tools
                                                      });

         _logger.Info($"Registered peer solution '{name}' on port {port}");

         // A peer registers when it opens a solution, and again when it swaps in a new tools build.
         NotifyToolsChanged();
      }

      return new JsonRpcResponse
             {
                Id = request.Id,
                Result = new JObject { ["ok"] = true }
             };
   }

   JsonRpcResponse HandlePeerDeregister(JsonRpcRequest request)
   {
      var path = request.Params?["solutionPath"]?.ToString();

      if(!string.IsNullOrEmpty(path))
      {
         _registrations.Locked(it => it.Peers.Remove(path));

         _logger.Info($"Deregistered peer solution at '{path}'");
      }

      return new JsonRpcResponse
             {
                Id = request.Id,
                Result = new JObject { ["ok"] = true }
             };
   }

   JsonRpcResponse HandleInternalStatus(JsonRpcRequest request)
   {
      var solutions = new JArray();

      var (local, peers) = Registered();
      foreach(var s in local)
         solutions.Add(LocalHealth(s));

      foreach(var p in peers)
      {
         solutions.Add(new JObject
                       {
                          ["name"] = p.SolutionName,
                          ["path"] = p.SolutionPath
                       });
      }

      return new JsonRpcResponse
             {
                Id = request.Id,
                Result = new JObject
                         {
                            ["port"] = Port,
                            ["role"] = IsPrimary ? "primary" : "peer",
                            ["solutions"] = solutions
                         }
             };
   }

   JsonRpcResponse HandleInternalRestart(JsonRpcRequest request)
   {
      Restart();
      return new JsonRpcResponse
             {
                Id = request.Id,
                Result = new JObject { ["ok"] = true }
             };
   }

   #endregion

   /// <summary>
   /// GET /health: a plain-JSON readiness check for session-start hooks, answered without MCP.
   /// Peers are asked with a short timeout; one that does not answer is reported as unreachable.
   /// </summary>
   void HandleHealth(HttpListenerContext context)
   {
      var (local, peers) = Registered();
      var solutions = local.Select(LocalHealth).Concat(peers.Select(PeerHealth)).ToList();
      var health = new JObject
                   {
                      ["solutions"] = new JArray(solutions),
                      ["indexing"] = solutions.Any(x => x["indexing"]?.Value<bool>() == true || x["healthFailed"] != null),
                      ["pendingDiskChanges"] = solutions.Sum(x => x["pendingDiskChanges"]?.Value<int>() ?? 0)
                   };

      var bytes = Encoding.UTF8.GetBytes(health.ToString(Formatting.None));
      context.Response.ContentType = "application/json";
      context.Response.ContentLength64 = bytes.Length;
      context.Response.OutputStream.Write(bytes, 0, bytes.Length);
      context.Response.Close();
   }

   JObject LocalHealth(SolutionRegistration solution)
   {
      var health = new JObject { ["name"] = solution.Name, ["path"] = solution.Path };
      try
      {
         health.Merge(solution.Health());
      }
      catch(Exception ex)
      {
         // The solution's state is unknown, which must not read as ready: see HandleHealth.
         health["healthFailed"] = ToolFailure.Logged(ex, $"the health of '{solution.Name}'");
      }

      return health;
   }

   JObject PeerHealth(PeerRegistration peer)
   {
      var fallback = new JObject
                     {
                        ["name"] = peer.SolutionName,
                        ["path"] = peer.SolutionPath,
                        ["reachable"] = false
                     };

      try
      {
         var webRequest = (HttpWebRequest)WebRequest.Create($"http://127.0.0.1:{peer.Port}/");
         webRequest.Method = "POST";
         webRequest.ContentType = "application/json";
         webRequest.Timeout = 1000;

         var body = Encoding.UTF8.GetBytes(
            "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"internal/status\",\"params\":{}}");
         webRequest.ContentLength = body.Length;
         using(var stream = webRequest.GetRequestStream())
            stream.Write(body, 0, body.Length);

         using(var response = (HttpWebResponse)webRequest.GetResponse())
         using(var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
         {
            var status = JObject.Parse(reader.ReadToEnd());
            return status["result"]?["solutions"]?
                  .OfType<JObject>()
                  .FirstOrDefault(x => x["path"]?.ToString() == peer.SolutionPath) ?? fallback;
         }
      }
      catch(Exception exception)
      {
         // Whatever the failure, the answer reports the peer as unreachable: that's the fallback.
         _logger.Verbose($"The peer on port {peer.Port} didn't answer: {exception.Message}");
         return fallback;
      }
   }

   // The work tree, not the .sln's folder: a solution in Client/TPS still owns files under Shared/.
   static string SolutionDirectory(string solutionPath) =>
      SolutionRoot.Of(Path.GetDirectoryName(solutionPath)).Workspace ?? solutionPath;

   /// <summary>
   /// An absolute path is held by the work tree it lies under; a relative one by a solution it exists in.
   /// </summary>
   static bool Holds(string solutionPath, string path)
   {
      if(Path.IsPathRooted(path))
         return PathIsUnder(path, SolutionDirectory(solutionPath));

      var absolute = SolutionRoot.Of(Path.GetDirectoryName(solutionPath)).Absolute(path);

      return absolute != null && (File.Exists(absolute) || Directory.Exists(absolute));
   }

   static bool PathIsUnder(string path, string directory)
   {
      var prefix = directory.Replace('\\', '/').TrimEnd('/') + "/";

      return path.Replace('\\', '/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
   }

   /// <summary>
   /// Checks if <paramref name="segment"/> appears as a complete path segment in <paramref name="path"/>.
   /// E.g. "tps-project" matches ".../tps-project/..." but NOT ".../tps-project-dyn/...".
   /// Also supports multi-segment queries like "tps-project/Client".
   /// </summary>
   static bool PathContainsSegment(string path, string segment)
   {
      var normalized = "/" + path.Replace("\\", "/") + "/";
      var search = "/" + segment.Replace("\\", "/") + "/";
      return normalized.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
   }

   /// <summary>
   /// For solutions with duplicate names, finds the first parent directory segment
   /// that uniquely identifies each solution. Returns a map of path → unique segment.
   /// </summary>
   static Dictionary<string, string> ComputeDisambiguators(List<NameAndPath> solutions)
   {
      var result = new Dictionary<string, string>();
      var groups = solutions.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase);

      foreach(var group in groups)
      {
         var items = group.ToList();
         if(items.Count <= 1) continue;

         foreach(var item in items)
         {
            var segments = item.Path.Replace("\\", "/").Split('/');
            // Walk from right to left, skipping the filename
            for(var i = segments.Length - 2; i >= 0; i--)
            {
               var seg = segments[i];
               if(string.IsNullOrEmpty(seg)) continue;

               var wrappedSeg = "/" + seg + "/";
               var matchCount = items.Count(other =>
                                               ("/" + other.Path.Replace("\\", "/") + "/")
                                              .IndexOf(wrappedSeg, StringComparison.OrdinalIgnoreCase) >= 0);

               if(matchCount == 1)
               {
                  result[item.Path] = seg;
                  break;
               }
            }
         }
      }

      return result;
   }

   static JsonRpcResponse ToolError(JsonRpcRequest request, string message)
   {
      return new JsonRpcResponse
             {
                Id = request.Id,
                Result = new CallToolResult
                         {
                            IsError = true,
                            Content = { new ContentBlock { Text = message } }
                         }
             };
   }
}

/// <summary>
/// An open event stream to a client. Writes come from its own keep-alive loop and from notifications, so they take
/// turns.
/// </summary>
internal sealed class ServerEventStream
{
   readonly IThreadShared<StreamWriter> _writer;

   public ServerEventStream(StreamWriter writer) => _writer = IThreadShared.New(writer);

   /// <summary>False when the client has gone, which fails the write in one of these ways.</summary>
   public bool TrySend(string text) =>
      _writer.Locked(writer =>
      {
         try
         {
            writer.Write(text);
            return true;
         }
         catch(Exception exception) when(exception is IOException or HttpListenerException or ObjectDisposedException)
         {
            return false;
         }
      });
}

internal class SolutionRegistration
{
   public string Name { get; set; }
   public string Path { get; set; }
   public Func<List<ToolDefinition>> ListTools { get; set; }
   public Func<string, JObject, string> CallTool { get; set; }
   public Func<JObject> Health { get; set; }
}

internal class PeerRegistration
{
   public string SolutionName { get; set; }
   public string SolutionPath { get; set; }
   public int Port { get; set; }
   public List<ToolDefinition> Tools { get; set; }
}

internal class SolutionTarget
{
   public string Name { get; set; }
   public string Path { get; set; }
   public bool IsLocal { get; set; }
   public int PeerPort { get; set; }
   public Func<string, JObject, string> CallTool { get; set; }
}

/// <summary>The solutions the server routes calls to: this process's, and the peers' that registered with it.</summary>
internal sealed class Registrations
{
   public readonly Dictionary<string, SolutionRegistration> Solutions = new();
   public readonly Dictionary<string, PeerRegistration> Peers = new();
}

internal class NameAndPath
{
   public string Name { get; set; }
   public string Path { get; set; }
}
