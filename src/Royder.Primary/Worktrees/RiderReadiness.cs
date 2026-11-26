using Newtonsoft.Json.Linq;
using Royder.Primary.Transport;

namespace Royder.Primary.Worktrees;

/// <summary>
///   Waits until the packaged host serves the original Git worktree with a tools build, and its indexing and source
///   generators are done.
/// </summary>
/// <remarks>
///   Any tools build the host serves will do: it serves the newest one built against it, from the shared folder of
///   everyday builds when the settings name one, which is newer than the pinned build once a tools change is deployed.
/// </remarks>
sealed class RiderReadiness(PrimarySettings settings, ToolCatalog catalog, RiderEndpoint backend)
{
   public async Task Wait(ManagedWorktree worktree, WarmUp warmUp, CancellationToken cancel)
   {
      while(true)
      {
         if(RegisteredWorktree.Find(worktree.Registration.Root, settings.Trusted) != worktree.Registration) throw new InvalidOperationException("The worktree disappeared while Rider was opening it.");
         var health = await backend.Health(cancel);
         var row = (health?["solutions"] as JArray)?.OfType<JObject>().SingleOrDefault(it =>
            RegisteredWorktree.SamePath((string?)it["path"], worktree.SolutionPath));
         var waitingFor = WhatRiderHasYetToDo(row);
         if(waitingFor == null) return;
         warmUp.WaitsFor(waitingFor);
         await Task.Delay(250, cancel);
      }
   }

   /// <summary>
   ///   What Rider has yet to do, by <paramref name="row"/>, its health of the solution, before it can answer for the
   ///   solution: null when nothing.
   /// </summary>
   string? WhatRiderHasYetToDo(JObject? row)
   {
      if(row == null) return "Rider's backend to register the solution";
      if(row["healthFailed"] != null) return "Rider to report the solution's state, which it failed to";
      if((bool?)row["indexing"] != false) return "Rider to finish indexing it";
      if((string?)row.SelectToken("tools.build") == null || (string?)row.SelectToken("tools.host") != catalog.HostHash) return "Rider to serve the staged tools";
      var generators = row.Property("sourceGeneratorsReady");
      if(generators == null) throw new InvalidOperationException("This managed Rider build does not report source generator readiness.");
      return generators.Value.Type == JTokenType.Null || (bool?)generators.Value == true
                ? null
                : "Rider to load its projects and run their source generators";
   }
}
