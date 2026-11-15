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
using DotNetAnalysisAndRefactoringMcp.Swapping;
using DotNetAnalysisAndRefactoringMcp.Threading;
using DotNetAnalysisAndRefactoringMcp.Tools;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   This Rider's MCP server: the HTTP listener, the clients' event streams, and the solutions registered with it, this
///   Rider's and the peers'. Every request it hands to the current tools build, which has the protocol.
/// </summary>
/// <remarks>
///   Only what must outlive a tools build is here, so that a change to the protocol is a deploy and not a Rider
///   restart: see "The host stays minimal" in the rule on developing the plugin.
/// </remarks>
public class McpHttpServer
{
   volatile HttpListener _listener;
   readonly ILogger _logger;
   readonly SwappableTools _swappableTools;
   readonly IThreadShared<Registrations> _registrations = ThreadShared.New(new Registrations());
   readonly string _sessionId;
   readonly IThreadShared<List<ServerEventStream>> _eventStreams = ThreadShared.New(new List<ServerEventStream>());
   Thread _listenerThread;
   volatile bool _running;

   public McpHttpServer(int port, int primaryPort, ILogger logger, SwappableTools swappableTools)
   {
      Port = port;
      PrimaryPort = primaryPort;
      _logger = logger;
      _swappableTools = swappableTools;
      _sessionId = Guid.NewGuid().ToString("N");
      _listener = new HttpListener();
      _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
   }

   /// <inheritdoc cref="IMcpServer.Port"/>
   public int Port { get; }

   /// <inheritdoc cref="IMcpServer.PrimaryPort"/>
   public int PrimaryPort { get; }

   /// <inheritdoc cref="IMcpServer.IsPrimary"/>
   public bool IsPrimary => Port == PrimaryPort;

   /// <summary>Registers a solution of this Rider, and registers it with the primary too when this server is a peer.</summary>
   public void RegisterSolution(McpServerComponent solution)
   {
      _registrations.Locked(it => it.Solutions[solution.FilePath] = solution);
      _logger.Info($"Registered solution '{solution.Name}' locally on port {Port}");
      if(!IsPrimary)
         WithThePrimary(solution, "registering with", (lease, server, leased) => lease.RegisterWithThePrimary(server, leased));
   }

   public void UnregisterSolution(McpServerComponent solution)
   {
      _registrations.Locked(it => it.Solutions.Remove(solution.FilePath));
      if(!IsPrimary)
         WithThePrimary(solution, "deregistering from", (lease, server, leased) => lease.DeregisterFromThePrimary(server, leased));
   }

   /// <summary>
   ///   The clients are told on the primary, which they connect to. A peer registers its solutions with the primary
   ///   again, with the new build's tools, and the primary tells the clients.
   /// </summary>
   public void ToolsSwapped()
   {
      if(IsPrimary)
      {
         NotifyToolsChanged();
         return;
      }

      foreach(var solution in LocalSolutions())
         WithThePrimary(solution, "registering with", (lease, server, leased) => lease.RegisterWithThePrimary(server, leased));
   }

   /// <summary>Copies this Rider's solutions to the server that takes over the primary's port from this peer.</summary>
   public void TransferRegistrationsTo(McpHttpServer target)
   {
      foreach(var solution in LocalSolutions())
         target.RegisterSolution(solution);
   }

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

   /// <summary>The solutions of this Rider.</summary>
   List<McpServerComponent> LocalSolutions() => _registrations.Locked(it => it.Solutions.Values.ToList());

   /// <summary>
   ///   Registers <paramref name="solution"/> with the primary, or deregisters it, through the current tools build.
   ///   Its failure is logged: the primary may have gone, which the watchdog's takeover handles, and until then the
   ///   primary lacks this solution.
   /// </summary>
   void WithThePrimary(McpServerComponent solution, string doing, Action<ToolsLease, IMcpServer, IMcpSolution> action)
   {
      try
      {
         using var lease = _swappableTools.Lease();
         action(lease, new LeasedServer(this, lease), solution.For(lease));
      }
      catch(Exception exception)
      {
         ToolFailure.Logged(exception, $"{doing} the primary MCP server on port {PrimaryPort}");
      }
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
               AnswerJson(context, Health());
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

   /// <summary>A JSON-RPC request, which the tools build answers; a notification gets 202 and no answer.</summary>
   void HandlePost(HttpListenerContext context)
   {
      string body;
      using(var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
      {
         body = reader.ReadToEnd();
      }

      if(Answer(body) is {} answer)
      {
         AnswerJson(context, answer);
         return;
      }

      context.Response.StatusCode = 202;
      context.Response.Close();
   }

   /// <summary>
   ///   The tools build's answer to the JSON-RPC request <paramref name="body"/>. When it fails, as when no tools build
   ///   loads, the answer is a JSON-RPC error that says why, which a client shows, where a 500 would say nothing.
   /// </summary>
   string Answer(string body)
   {
      try
      {
         using var lease = _swappableTools.Lease();
         return lease.Answer(new LeasedServer(this, lease), body);
      }
      catch(Exception exception)
      {
         var id = JObject.Parse(body)["id"];
         var failure = ToolFailure.Logged(exception, "an MCP request");
         if(id is null or { Type: JTokenType.Null }) return null;

         return new JObject
                {
                   ["jsonrpc"] = "2.0",
                   ["id"] = id,
                   ["error"] = new JObject { ["code"] = -32603, ["message"] = failure }
                }.ToString(Formatting.None);
      }
   }

   /// <summary>
   ///   <c>GET /health</c>, a plain-JSON readiness check that answers without MCP, from the tools build. When it fails,
   ///   as when no tools build loads, it lists no solutions, and says why and what the tools builds' swapping reports.
   /// </summary>
   string Health()
   {
      try
      {
         using var lease = _swappableTools.Lease();
         return lease.Health(new LeasedServer(this, lease));
      }
      catch(Exception exception)
      {
         return new JObject
                {
                   ["solutions"] = new JArray(),
                   ["indexing"] = false,
                   ["pendingDiskChanges"] = 0,
                   ["healthFailed"] = ToolFailure.Logged(exception, "the health report"),
                   ["tools"] = _swappableTools.Status()
                }.ToString(Formatting.None);
      }
   }

   static void AnswerJson(HttpListenerContext context, string json)
   {
      var bytes = Encoding.UTF8.GetBytes(json);
      context.Response.ContentType = "application/json";
      context.Response.ContentLength64 = bytes.Length;
      context.Response.OutputStream.Write(bytes, 0, bytes.Length);
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
   static void HandleDeleteSession(HttpListenerContext context)
   {
      context.Response.StatusCode = 200;
      context.Response.Close();
   }

   /// <summary>The server as one request on a <see cref="ToolsLease"/>'s tools build sees it: the registrations as they were when it came.</summary>
   sealed class LeasedServer : IMcpServer
   {
      readonly McpHttpServer _server;

      public LeasedServer(McpHttpServer server, ToolsLease lease)
      {
         _server = server;
         (Solutions, Peers) = server._registrations.Locked(it => ((IReadOnlyList<IMcpSolution>)it.Solutions.Values.Select(solution => solution.For(lease)).ToList(),
                                                                  (IReadOnlyList<PeerRegistration>)it.Peers.Values.ToList()));
      }

      public int Port => _server.Port;

      public int PrimaryPort => _server.PrimaryPort;

      public bool IsPrimary => _server.IsPrimary;

      public IReadOnlyList<IMcpSolution> Solutions { get; }

      public IReadOnlyList<PeerRegistration> Peers { get; }

      public JObject ToolsBuilds => _server._swappableTools.Status();

      public void RegisterPeer(PeerRegistration peer) => _server._registrations.Locked(it => it.Peers[peer.SolutionPath] = peer);

      public void DeregisterPeer(string solutionPath) => _server._registrations.Locked(it => it.Peers.Remove(solutionPath));

      public void RemovePeersOn(int port) =>
         _server._registrations.Locked(it =>
         {
            foreach(var path in it.Peers.Values.Where(peer => peer.Port == port).Select(peer => peer.SolutionPath).ToList())
               it.Peers.Remove(path);
         });

      public void NotifyToolsChanged() => _server.NotifyToolsChanged();

      public void Restart() => _server.Restart();
   }

   /// <summary>The solutions the server routes calls to: this Rider's, and the peers' that registered with it.</summary>
   sealed class Registrations
   {
      /// <summary>This Rider's solutions, by their file paths.</summary>
      public readonly Dictionary<string, McpServerComponent> Solutions = new();

      /// <summary>The peers' solutions, by their file paths.</summary>
      public readonly Dictionary<string, PeerRegistration> Peers = new();
   }
}

/// <summary>
/// An open event stream to a client. Writes come from its own keep-alive loop and from notifications, so they take
/// turns.
/// </summary>
sealed class ServerEventStream
{
   readonly IThreadShared<StreamWriter> _writer;

   public ServerEventStream(StreamWriter writer) => _writer = ThreadShared.New(writer);

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
