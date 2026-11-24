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
   /// <summary>The backends the last check couldn't verify, for the timeout's message.</summary>
   IReadOnlyList<string> _unverified = [];

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

   /// <summary>How many ports the cluster's servers take, from its base port on: as many as a backend tries when binding.</summary>
   const int PortsOfTheCluster = 10;

   /// <summary>
   ///   The backends of the private Rider that serve a solution, each by its own word: the server on one of the
   ///   cluster's ports that answers <c>internal/status</c> with the process ID of a live backend of the job, and its
   ///   local solution.
   /// </summary>
   /// <remarks>
   ///   Until 2026-11-25 the primary read this from each backend's log: the line announcing its port, and the line
   ///   naming the solution it opened. The log of the backend that opened <c>~/.claude</c> at 22:38 on 2026-11-24 had
   ///   no line announcing its port, and Rider keeps a backend's log, verbose here, in seven files of 50 MB: one backend
   ///   wrote 150 MB in an hour and a half, after which its first lines are gone. A backend it could not identify made
   ///   its own open wait four minutes and fail, and every close after it wait four minutes for it and fail.
   /// </remarks>
   public async Task<IReadOnlyList<RiderGeneration.Backend>> Backends(CancellationToken cancellation)
   {
      var processes = generation.LiveBackendProcesses();
      if(processes.Count == 0) return [];
      var ports = Enumerable.Range(generation.Launch.RoyderPort, PortsOfTheCluster).ToArray();
      var answers = await Task.WhenAll(ports.Select(port => Call(port, "internal/status", new {}, cancellation))).ConfigureAwait(false);
      var found = new List<RiderGeneration.Backend>();
      foreach(var (port, status) in ports.Zip(answers))
      {
         if(status?["pid"]?.GetValue<int>() is not {} pid || processes.FirstOrDefault(it => it.Pid == pid) is not {} process) continue;
         var local = (status["solutions"] as JsonArray)?.FirstOrDefault(it => it?["local"]?.GetValue<bool>() == true);
         if(local?["path"]?.GetValue<string>() is {} solution) found.Add(new RiderGeneration.Backend(solution, port, process));
      }
      return found;
   }

   /// <summary>Polls the local identities reported by the current primary. A surviving peer registers only once on
   /// solution open. A promoted primary may therefore need its siblings' registrations restored.</summary>
   /// <remarks>
   ///   For an open, <paramref name="requested"/>, only its own backend has to be verified. Until 2026-11-24 every
   ///   backend had to be: one whose worktree Git had removed under it kept every other open in the JVM waiting out
   ///   the four minutes, and three agents got "still opening" for 10 to 40 minutes.
   /// </remarks>
   public async Task WaitForRouting(string? requested, CancellationToken cancellation, string? closed = null, string[]? siblings = null)
   {
      var timer = Stopwatch.StartNew();
      while(timer.Elapsed < TimeSpan.FromMinutes(4))
      {
         cancellation.ThrowIfCancellationRequested();
         generation.AssertRunning();
         var backends = await Backends(cancellation).ConfigureAwait(false);
         var siblingsPresent = siblings == null || siblings.All(path => backends.Any(it => RiderGeneration.Same(it.Solution,path)));
         if(siblingsPresent && requested == null && backends.Count == 0) return;
         if(siblingsPresent && (requested == null || backends.Any(it => RiderGeneration.Same(it.Solution,requested))) &&
            await RepairAndCheck(backends, requested, closed, cancellation).ConfigureAwait(false)) return;
         await Task.Delay(500,cancellation).ConfigureAwait(false);
      }
      throw new TimeoutException($"Private Royder cluster at {generation.Launch.RoyderPort} did not verify its registrations and indexed build for '{requested ?? "the remaining solutions"}'"
                               + (_unverified.Count > 0 ? $": unverified {string.Join(", ", _unverified)}." : "."));
   }

   async Task<bool> RepairAndCheck(IReadOnlyList<RiderGeneration.Backend> backends, string? requested, string? closed, CancellationToken cancellation)
   {
      var checkedBackends = requested == null ? backends : [.. backends.Where(it => RiderGeneration.Same(it.Solution, requested))];
      _unverified = [.. checkedBackends.Select(it => it.Solution)];
      var port = generation.Launch.RoyderPort;
      var primary = await Call(port, "internal/status", new {}, cancellation).ConfigureAwait(false);
      if(primary?["role"]?.GetValue<string>() != "primary" || primary["solutions"] is not JsonArray registrations) return false;
      var local = registrations.FirstOrDefault(generation.Serves);
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
      foreach(var backend in checkedBackends)
      {
         if(solutions.Any(it => generation.Serves(it) && RiderGeneration.Same(backend.Solution,it?["path"]?.GetValue<string>()))) continue;
         if(backend.Port == port || !backend.Process.IsAlive()) return false;
         var peer = await Call(backend.Port,"internal/status",new {},cancellation).ConfigureAwait(false);
         if(peer?["role"]?.GetValue<string>() != "peer" || peer["solutions"] is not JsonArray peerSolutions) return false;
         var owned = peerSolutions.FirstOrDefault(it => generation.Serves(it) && RiderGeneration.Same(backend.Solution,it?["path"]?.GetValue<string>()));
         if(owned == null) return false;
         var list = await Call(backend.Port,"tools/list",new {},cancellation).ConfigureAwait(false);
         if(list?["tools"] is not JsonArray advertised) return false;
         var tools = new JsonArray();
         foreach(var tool in advertised.OfType<JsonObject>().Where(it => it["name"]?.GetValue<string>() != "list_solutions"))
         {
            var copy = tool.DeepClone();
            (copy["inputSchema"]?["properties"] as JsonObject)?.Remove("solutionName");
            tools.Add(copy);
         }
         await Call(port,"internal/register",new { solutionName=owned["name"]?.GetValue<string>(), solutionPath=backend.Solution,
                                                   port=backend.Port, tools },cancellation).ConfigureAwait(false);
         return false; // Verify from the primary on a fresh sample.
      }

      _unverified = [.. checkedBackends.Where(backend => !solutions.Any(row => RiderGeneration.Same(backend.Solution,row?["path"]?.GetValue<string>()) &&
                                                                         generation.Serves(row)))
                                       .Select(it => it.Solution)];
      return _unverified.Count == 0;
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
