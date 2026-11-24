using System.Net.Sockets;
using DotNetAnalysisAndRefactoringMcp;
using Newtonsoft.Json.Linq;
using Royder.Primary.Worktrees;

namespace Royder.Primary.Transport;

/// <summary>
///   The tools the primary lists: those ordinary Rider and the private Rider serve now, the pinned catalog's for a cold
///   start, and explicit worktree lifecycle operations.
/// </summary>
/// <remarks>
///   The Riders' own lists come first, since the pinned catalog was captured from the package's build, and they serve
///   the newest build deployed since.
/// </remarks>
sealed class PrimaryToolCatalog(ToolCatalog pinned, OrdinaryRiders ordinary, ManagedWorktrees worktrees, RiderEndpoint backend)
{
   public const string Warm = "warm_worktree";
   public const string Close = "close_worktree";
   public const string OpenOnDemand = "An absolute filePath or .sln/.slnx solutionName opens its checkout, even with desktop Rider closed.";
   public const string Instructions = McpInstructions.Purpose + " " + OpenOnDemand;

   const string SolutionName = "Full .sln/.slnx path to open a trusted checkout on demand, or an open solution's name or unique path segment. "
                             + "Omit when file paths identify the target or it is the only open solution.";

   public async Task<JArray> Listed(CancellationToken cancel)
   {
      var desktop = await ordinary.Tools(cancel) ?? [];
      var managed = await ManagedTools(cancel) ?? [];
      var tools = desktop.Concat(managed).Concat(pinned.Tools).OfType<JObject>().DistinctBy(it => (string?)it["name"]);
      var listed = new JArray(tools,
         ManagementTool(Warm, "Optionally open a trusted checkout ahead of the first analysis call. Pass its absolute solution filename and wait for readiness."),
         ManagementTool(Close, "Close the managed Rider project. Answers Closed once its backend and workers exited, or Still closing: call again to wait on. The worktree stays on disk."));
      listed.OfType<JObject>().Single(it => (string?)it["name"] == "list_solutions")["description"] =
         "List currently open solutions. " + OpenOnDemand;
      foreach(var tool in listed.OfType<JObject>())
         if(tool.SelectToken("inputSchema.properties.solutionName") is JObject selector)
            selector["description"] = SolutionName;
      return listed;
   }

   /// <summary>Whether a call to <paramref name="tool"/> can be served: the pinned catalog has it, or a Rider lists it now.</summary>
   public async Task<bool> Provides(string tool, CancellationToken cancel) =>
      pinned.Provides(tool) || (await Listed(cancel)).OfType<JObject>().Any(it => (string?)it["name"] == tool);

   /// <summary>The private Rider's tools, while it has a managed worktree open.</summary>
   async Task<JArray?> ManagedTools(CancellationToken cancel)
   {
      if(worktrees.Open.Length == 0) return null;
      try { return await backend.Catalog(cancel); }
      catch(HttpRequestException ex) when(ex.GetBaseException() is SocketException { SocketErrorCode: SocketError.ConnectionRefused }) { return null; } // It closed since.
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
