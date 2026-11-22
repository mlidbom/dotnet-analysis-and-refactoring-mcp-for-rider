using Newtonsoft.Json.Linq;

namespace Royder.Primary.Transport;

/// <summary>The pinned headless tools, any additional ordinary Rider tools, and explicit worktree lifecycle operations.</summary>
sealed class PrimaryToolCatalog(ToolCatalog pinned, OrdinaryRiders ordinary)
{
   public const string Warm = "warm_worktree";
   public const string Close = "close_worktree";

   public async Task<JArray> Listed(CancellationToken cancel)
   {
      var desktop = await ordinary.Tools(cancel) ?? [];
      var tools = desktop.Concat(pinned.Tools).OfType<JObject>().DistinctBy(it => (string?)it["name"]);
      return new JArray(tools,
         ManagementTool(Warm, "Open a trusted main checkout or linked worktree in the shared private Rider and wait for it to be ready. Pass its absolute solution filename."),
         ManagementTool(Close, "Close the managed Rider project and await its workers' exit. The worktree stays on disk."));
   }

   static JObject ManagementTool(string name, string description) => new()
   {
      ["name"] = name, ["description"] = description,
      ["inputSchema"] = new JObject { ["type"] = "object", ["properties"] = new JObject
         { ["solutionPath"] = new JObject { ["type"] = "string", ["description"] = "Absolute path to the worktree's .sln or .slnx." } },
         ["required"] = new JArray("solutionPath") },
      ["annotations"] = new JObject { ["readOnlyHint"] = false, ["destructiveHint"] = false, ["idempotentHint"] = true, ["openWorldHint"] = false }
   };
}
