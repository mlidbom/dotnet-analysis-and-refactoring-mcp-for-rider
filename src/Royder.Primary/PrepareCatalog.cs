using Royder.Primary.Hosting;
using Royder.Primary.Transport;
using Royder.Primary.Worktrees;

namespace Royder.Primary;

/// <summary>Produces the cold tool catalog from the staged host and tools before enabling the public listener.</summary>
static class PrepareCatalog
{
   public static async Task Run(PrimarySettings settings, HttpClient http, string solution, CancellationToken cancel)
   {
      solution = Path.GetFullPath(solution);
      var tree = RegisteredWorktree.Find(solution, settings.GitDirectories) ?? throw new ArgumentException("Use a configured main checkout or one of its linked worktrees.");
      await tree.SolutionFor([], solution, cancel);
      using var owner = WorktreeJournal.OwnProfile(settings.Rider.ProfileDirectory);
      var desktop = (await new OrdinaryRiders(http, settings).Solutions(cancel))
         .FirstOrDefault(it => RegisteredWorktree.SamePath(it.WorkTree, tree.Root));
      if(desktop != null)
      {
         await ToolCatalog.Capture(desktop.Endpoint, settings.Rider.ToolsBuild, settings.CatalogFile, cancel);
         return;
      }
      var journal = new WorktreeJournal(settings.Rider.ProfileDirectory);
      var host = new SharedRiderHost(settings.Rider, http);
      var endpoint = new RiderEndpoint(http, settings.Rider.BackendMcpPort);
      await journal.Recover(host, settings, cancel);
      try
      {
         journal.Opening(tree, solution);
         await host.OpenAsync(tree, solution, cancel);
         await ToolCatalog.Capture(endpoint, settings.Rider.ToolsBuild, settings.CatalogFile, cancel);
      }
      finally
      {
         await journal.Recover(host, settings, CancellationToken.None);
      }
   }
}
