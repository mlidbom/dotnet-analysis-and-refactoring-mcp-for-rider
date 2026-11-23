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
   public async Task Wait(ManagedWorktree worktree, CancellationToken cancel)
   {
      while(true)
      {
         if(RegisteredWorktree.Find(worktree.Registration.Root, settings.GitDirectories) != worktree.Registration) throw new InvalidOperationException("The worktree disappeared while Rider was opening it.");
         var health = await backend.Health(cancel);
         var row = (health?["solutions"] as JArray)?.OfType<JObject>().SingleOrDefault(it =>
            RegisteredWorktree.SamePath((string?)it["path"], worktree.SolutionPath));
         if(row != null && (bool?)row["indexing"] == false && row["healthFailed"] == null &&
            (string?)row.SelectToken("tools.build") != null &&
            (string?)row.SelectToken("tools.host") == catalog.HostHash)
         {
            var generators = row.Property("sourceGeneratorsReady");
            if(generators == null) throw new InvalidOperationException("This managed Rider build does not report source generator readiness.");
            if(generators.Value.Type == JTokenType.Null || (bool?)generators.Value == true) return;
         }
         await Task.Delay(250, cancel);
      }
   }
}
