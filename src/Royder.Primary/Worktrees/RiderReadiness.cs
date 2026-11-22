using Newtonsoft.Json.Linq;
using Royder.Primary.Transport;

namespace Royder.Primary.Worktrees;

/// <summary>Waits for the requested tools build, project indexing and source generators in the original Git worktree.</summary>
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
            (string?)row.SelectToken("tools.build") == settings.Rider.ToolsBuild &&
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
