using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using JetBrains.Application;
using JetBrains.Application.Parts;
using JetBrains.Lifetimes;
using JetBrains.Util;
using Newtonsoft.Json.Linq;
using DotNetAnalysisAndRefactoringMcp.Swapping;
using DotNetAnalysisAndRefactoringMcp.Tools;

namespace DotNetAnalysisAndRefactoringMcp;

[ShellComponent(Instantiation.ContainerAsyncAnyThreadSafe)]
public class McpShellComponent : IDisposable
{
   const int DefaultPort = 23751;
   const int MaxPortAttempts = 10;
   McpHttpServer _server;
   readonly ILogger _logger;
   readonly SwappableTools _swappableTools;
   readonly int _primaryPort;

   public int Port => _server?.Port ?? 0;

   public McpShellComponent(Lifetime lifetime, ILogger logger, SwappableTools swappableTools)
   {
      _logger = logger;
      _swappableTools = swappableTools;
      var basePort = GetPort();
      _primaryPort = basePort;

      // Try to bind the primary port; if taken, try subsequent ports
      for(var attempt = 0; attempt < MaxPortAttempts; attempt++)
      {
         var tryPort = basePort + attempt;
         var tryServer = new McpHttpServer(tryPort, basePort, logger, swappableTools);
         try
         {
            tryServer.Start();
            _server = tryServer;

            if(_server.IsPrimary)
               _logger.Info($"MCP primary server started on port {tryPort}");
            else
               _logger.Info($"MCP peer server started on port {tryPort} (primary on {basePort})");

            // Watchdog: periodically verify the server is responsive
            var watchdog = new Timer(_ => Watchdog(),
                                     null,
                                     TimeSpan.FromSeconds(60),
                                     TimeSpan.FromSeconds(30));
            lifetime.OnTermination(() => watchdog.Dispose());

            break;
         }
         catch(HttpListenerException ex)
         {
            // The port is taken, by another Rider's server or anything else: the next one is tried.
            tryServer.Stop();
            if(attempt == MaxPortAttempts - 1)
               _logger.Error(ex, $"Failed to bind any port in range {basePort}-{tryPort} for MCP server");
            else
               _logger.Info($"Port {tryPort} unavailable, trying next...");
         }
      }

      swappableTools.Swapped += OnToolsSwapped;
      lifetime.OnTermination(() => swappableTools.Swapped -= OnToolsSwapped);
      lifetime.OnTermination(this);
   }

   /// <summary>The tools build served changed: <see cref="McpHttpServer.ToolsSwapped"/>.</summary>
   void OnToolsSwapped() => _server?.ToolsSwapped();

   public void RegisterSolution(McpServerComponent solution) => _server?.RegisterSolution(solution);

   public void UnregisterSolution(McpServerComponent solution) => _server?.UnregisterSolution(solution);

   public void Dispose()
   {
      _server?.Stop();
      _logger.Info("MCP server stopped");
   }

   void WatchdogPing()
   {
      if(_server == null) return;

      // For peers: check if the primary is still alive; if not, try to take over
      if(!_server.IsPrimary)
      {
         TryPromoteToPrimary();
         return;
      }

      // For primary: verify own listener is responsive
      try
      {
         PingServer(_server.Port);
      }
      catch(Exception ex) when(ex is WebException or IOException)
      {
         _logger.Warn($"MCP server health check failed: {ex.Message} — triggering restart");
         _server.Restart();
      }
   }

   /// <summary>The watchdog timer's callback: a thread entry, where anything that escapes ends Rider's backend.</summary>
   void Watchdog()
   {
      try
      {
         WatchdogPing();
      }
      catch(Exception exception)
      {
         ToolFailure.Logged(exception, "the MCP server's watchdog");
      }
   }

   /// <summary>
   /// For peer instances: checks if the primary port is available and attempts
   /// to take it over, becoming the new primary.
   /// </summary>
   void TryPromoteToPrimary()
   {
      try
      {
         // Check if the primary is still responding
         PingServer(_primaryPort);
         // Primary is alive — stay as peer
      }
      catch(Exception unreachable) when(unreachable is WebException or IOException)
      {
         // Primary is unreachable — try to take over
         _logger.Info($"Primary on port {_primaryPort} is unreachable — attempting to take over");

         try
         {
            var newServer = new McpHttpServer(_primaryPort, _primaryPort, _logger, _swappableTools);
            newServer.Start();

            // Transfer all solution registrations from old server to new server
            var oldServer = _server;
            oldServer.TransferRegistrationsTo(newServer);

            _server = newServer;

            oldServer.Stop();

            _logger.Info($"Promoted to primary on port {_primaryPort} (was peer on {oldServer.Port})");
         }
         catch(Exception ex)
         {
            // This Rider stays a peer, and the watchdog tries again in 30 s.
            ToolFailure.Logged(ex, $"taking over the primary port {_primaryPort}");
         }
      }
   }

   static void PingServer(int port)
   {
      var url = $"http://127.0.0.1:{port}/";
      var request = (HttpWebRequest)WebRequest.Create(url);
      request.Method = "POST";
      request.ContentType = "application/json";
      request.Timeout = 5000;

      var body = Encoding.UTF8.GetBytes(
         "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"internal/status\",\"params\":{}}");
      request.ContentLength = body.Length;
      using(var stream = request.GetRequestStream())
         stream.Write(body, 0, body.Length);

      using(var response = (HttpWebResponse)request.GetResponse())
      using(var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
         reader.ReadToEnd();
   }

   static int GetPort()
   {
      var envPort = Environment.GetEnvironmentVariable("DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT");
      if(!string.IsNullOrEmpty(envPort) && int.TryParse(envPort, out var port))
         return port;

      var installedPrimarySettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                                  "DotNetAnalysisAndRefactoringMcp", "primary", "settings.json");
      if(!File.Exists(installedPrimarySettings)) return DefaultPort;
      var settings = JObject.Parse(File.ReadAllText(installedPrimarySettings));
      var desktopPort = (int?)settings.GetValue("desktopPort", StringComparison.OrdinalIgnoreCase) ?? DefaultPort;
      if(desktopPort < 1 || desktopPort > 65536 - MaxPortAttempts)
         throw new InvalidDataException($"Invalid desktopPort in {installedPrimarySettings}: {desktopPort}");
      return desktopPort;
   }
}
