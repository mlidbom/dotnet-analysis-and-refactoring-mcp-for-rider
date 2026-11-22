using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>The effects an MCP client can expect from any call to a tool, including calls without a dry run.</summary>
/// <remarks>
///   Sent with each tool in <c>tools/list</c>, as the MCP specification's tool annotations. Clients decide by them what
///   needs approval: Codex sends every call of a tool not marked read-only through its approvals reviewer, and Claude
///   Code runs read-only calls in parallel and retries one after a dropped connection. They are hints, which no client
///   relies on for safety.
/// </remarks>
public sealed class ToolAnnotations
{
   /// <summary>Changes nothing: not the files, not Rider's model of the solution.</summary>
   public static ToolAnnotations ReadOnly { get; } = new(readOnly: true, openWorld: false);

   /// <summary>Edits source files only by adding to them, such as a missing using directive or a generated member.</summary>
   public static ToolAnnotations AdditiveEdit { get; } = new(readOnly: false, openWorld: false, destructive: false);

   /// <summary>Edits the solution's code in place, changing or removing what is there, as a rename does.</summary>
   public static ToolAnnotations SolutionEdit { get; } = new(readOnly: false, openWorld: false, destructive: true);

   /// <summary>
   ///   Changes state through code that isn't ours, which may reach beyond the solution: the quick fixes and
   ///   suggestions installed in ReSharper, and the project system's reload.
   /// </summary>
   public static ToolAnnotations OpenWorldChange { get; } = new(readOnly: false, openWorld: true, destructive: true);

   ToolAnnotations(bool readOnly, bool openWorld, bool? destructive = null)
   {
      ReadOnlyHint = readOnly;
      OpenWorldHint = openWorld;
      DestructiveHint = destructive;
   }

   /// <summary>MCP's <c>readOnlyHint</c>: the tool doesn't change its environment.</summary>
   [JsonProperty("readOnlyHint")]
   public bool ReadOnlyHint { get; }

   /// <summary>MCP's <c>openWorldHint</c>: the tool may interact with things outside the solution.</summary>
   [JsonProperty("openWorldHint")]
   public bool OpenWorldHint { get; }

   /// <summary>
   ///   MCP's <c>destructiveHint</c>, for a tool that changes state: whether it may change or remove what exists,
   ///   rather than only add. Left out for a read-only tool, which the specification says it doesn't apply to.
   /// </summary>
   [JsonProperty("destructiveHint", NullValueHandling = NullValueHandling.Ignore)]
   public bool? DestructiveHint { get; }

   /// <summary>
   ///   MCP's <c>idempotentHint</c>: false for every tool that changes state, since none promises that repeating a call
   ///   has no further effect. Left out for a read-only tool.
   /// </summary>
   [JsonProperty("idempotentHint", NullValueHandling = NullValueHandling.Ignore)]
   public bool? IdempotentHint => ReadOnlyHint ? null : false;
}
