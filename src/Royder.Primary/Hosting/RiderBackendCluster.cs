using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace Royder.Primary.Hosting;

/// <summary>Waits for the private Royder cluster to route to the right indexed backend, including after primary promotion.</summary>
internal sealed class RiderBackendCluster(HttpClient http, RiderGeneration generation)
{
   bool MatchesBuild(JsonNode? solution) =>
      solution?["tools"]?["build"]?.GetValue<string>() == generation.Launch.Build &&
      RiderGeneration.Same(solution["tools"]?["folder"]?.GetValue<string>(), generation.Launch.Tools) &&
      RiderGeneration.Same(solution["tools"]?["host"]?.GetValue<string>(), generation.Launch.HostHash);

   async Task<JsonNode?> Call(int port, string method, object parameters, CancellationToken cancellation)
   {
      using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
      call.CancelAfter(TimeSpan.FromSeconds(5));
      try
      {
         using var response = await http.PostAsJsonAsync($"http://127.0.0.1:{port}/",
            new { jsonrpc = "2.0", id = 1, method, @params = parameters },call.Token).ConfigureAwait(false);
         if(!response.IsSuccessStatusCode) return null;
         return (await response.Content.ReadFromJsonAsync<JsonObject>(call.Token).ConfigureAwait(false))?["result"];
      }
      catch(HttpRequestException) { return null; } // The surviving peer's watchdog may still be taking over the base port.
      catch(OperationCanceledException) when(!cancellation.IsCancellationRequested) { return null; }
   }

   /// <summary>Polls the local identities reported by the current primary. A surviving peer registers only once on
   /// solution open. A promoted primary may therefore need its siblings' registrations restored.</summary>
   public async Task WaitForRouting(string? requested, CancellationToken cancellation, string? closed = null, string[]? siblings = null)
   {
      var timer = Stopwatch.StartNew();
      while(timer.Elapsed < TimeSpan.FromMinutes(4))
      {
         cancellation.ThrowIfCancellationRequested();
         generation.AssertRunning();
         var backends = generation.Backends();
         var siblingsPresent = siblings == null || siblings.All(path => backends.Any(it => RiderGeneration.Same(it.Solution,path)));
         if(siblingsPresent && requested == null && backends.Count == 0) return;
         if(siblingsPresent && (requested == null || backends.Any(it => RiderGeneration.Same(it.Solution,requested))) &&
            await RepairAndCheck(backends, closed, cancellation).ConfigureAwait(false)) return;
         await Task.Delay(500,cancellation).ConfigureAwait(false);
      }
      throw new TimeoutException($"Private Royder cluster at {generation.Launch.RoyderPort} did not verify its registrations and indexed build for '{requested ?? "the remaining solutions"}'.");
   }

   async Task<bool> RepairAndCheck(IReadOnlyList<RiderGeneration.Backend> backends, string? closed, CancellationToken cancellation)
   {
      var port = generation.Launch.RoyderPort;
      var primary = await Call(port, "internal/status", new {}, cancellation).ConfigureAwait(false);
      if(primary?["role"]?.GetValue<string>() != "primary" || primary["solutions"] is not JsonArray registrations) return false;
      var local = registrations.FirstOrDefault(it => MatchesBuild(it));
      if(local == null || !backends.Any(it => RiderGeneration.Same(it.Solution,local["path"]?.GetValue<string>()))) return false;
      if(closed != null && registrations.Any(it => RiderGeneration.Same(closed,it?["path"]?.GetValue<string>())))
      {
         // The caller has already proved the old backend and its descendants are gone.
         if(backends.Any(it => RiderGeneration.Same(it.Solution,closed))) return false;
         await Call(port,"internal/deregister",new { solutionPath=closed },cancellation).ConfigureAwait(false);
         return false;
      }
      var solutions = await Health(port,cancellation).ConfigureAwait(false);
      if(solutions == null) return false;
      foreach(var backend in backends)
      {
         if(solutions.Any(it => MatchesBuild(it) && RiderGeneration.Same(backend.Solution,it?["path"]?.GetValue<string>()))) continue;
         if(backend.InitialPort == port || !backend.Process.IsAlive()) return false;
         var peer = await Call(backend.InitialPort,"internal/status",new {},cancellation).ConfigureAwait(false);
         if(peer?["role"]?.GetValue<string>() != "peer" || peer["solutions"] is not JsonArray peerSolutions) return false;
         var owned = peerSolutions.FirstOrDefault(it => MatchesBuild(it) && RiderGeneration.Same(backend.Solution,it?["path"]?.GetValue<string>()));
         if(owned == null) return false;
         var list = await Call(backend.InitialPort,"tools/list",new {},cancellation).ConfigureAwait(false);
         if(list?["tools"] is not JsonArray advertised) return false;
         var tools = new JsonArray();
         foreach(var tool in advertised.OfType<JsonObject>().Where(it => it["name"]?.GetValue<string>() != "list_solutions"))
         {
            var copy = tool.DeepClone();
            (copy["inputSchema"]?["properties"] as JsonObject)?.Remove("solutionName");
            tools.Add(copy);
         }
         await Call(port,"internal/register",new { solutionName=owned["name"]?.GetValue<string>(), solutionPath=backend.Solution,
                                                   port=backend.InitialPort, tools },cancellation).ConfigureAwait(false);
         return false; // Verify from the primary on a fresh sample.
      }

      return backends.All(backend => solutions.Any(row => RiderGeneration.Same(backend.Solution,row?["path"]?.GetValue<string>()) &&
         MatchesBuild(row)));
   }

   async Task<JsonArray?> Health(int port,CancellationToken cancellation)
   {
      using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
      call.CancelAfter(TimeSpan.FromSeconds(8));
      try
      {
         var health = await http.GetFromJsonAsync<JsonObject>($"http://127.0.0.1:{port}/health",call.Token).ConfigureAwait(false);
         return health?["solutions"] as JsonArray;
      }
      catch(HttpRequestException) { return null; }
      catch(OperationCanceledException) when(!cancellation.IsCancellationRequested) { return null; }
   }
}
