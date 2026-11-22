using Newtonsoft.Json.Linq;

namespace Royder.Primary.Transport;

/// <summary>The pinned headless tools, any additional ordinary Rider tools, and explicit worktree lifecycle operations.</summary>
sealed class PrimaryToolCatalog(ToolCatalog pinned, OrdinaryRiders ordinary)
{
   public const string Warm = "warm_worktree";
   public const string Close = "close_worktree";
   public const string OpenOnDemand = "An absolute filePath or full .sln/.slnx path as solutionName opens a trusted checkout on demand, even with desktop Rider closed.";
   public const string Instructions = "ReSharper navigation, inspections and refactoring for C# and other .NET languages. " + OpenOnDemand;

   const string SolutionName = "Full .sln/.slnx path to open a trusted checkout on demand, or an open solution's name or unique path segment. "
                             + "Omit when file paths identify the target or it is the only open solution.";

   public async Task<JArray> Listed(CancellationToken cancel)
   {
      var desktop = await ordinary.Tools(cancel) ?? [];
      var tools = desktop.Concat(pinned.Tools).OfType<JObject>().DistinctBy(it => (string?)it["name"]);
      var listed = new JArray(tools,
         ManagementTool(Warm, "Optionally open a trusted checkout ahead of the first analysis call. Pass its absolute solution filename and wait for readiness."),
         ManagementTool(Close, "Close the managed Rider project and await its workers' exit. The worktree stays on disk."));
      listed.OfType<JObject>().Single(it => (string?)it["name"] == "list_solutions")["description"] =
         "List currently open solutions. " + OpenOnDemand;
      foreach(var tool in listed.OfType<JObject>())
         if(tool.SelectToken("inputSchema.properties.solutionName") is JObject selector && pinned.Provides((string)tool["name"]!))
            selector["description"] = SolutionName;
      return listed;
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
