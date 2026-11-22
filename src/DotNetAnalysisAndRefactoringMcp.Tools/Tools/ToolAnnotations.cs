using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>The effects an MCP client can expect from any call to a tool, including calls without a dry run.</summary>
public sealed class ToolAnnotations
{
   public static ToolAnnotations ReadOnly { get; } = new(readOnly: true, openWorld: false);
   public static ToolAnnotations AdditiveEdit { get; } = new(readOnly: false, openWorld: false, destructive: false);
   public static ToolAnnotations SolutionEdit { get; } = new(readOnly: false, openWorld: false, destructive: true);
   public static ToolAnnotations OpenWorldChange { get; } = new(readOnly: false, openWorld: true, destructive: true);

   ToolAnnotations(bool readOnly, bool openWorld, bool? destructive = null)
   {
      ReadOnlyHint = readOnly;
      OpenWorldHint = openWorld;
      DestructiveHint = destructive;
   }

   [JsonProperty("readOnlyHint")]
   public bool ReadOnlyHint { get; }

   [JsonProperty("openWorldHint")]
   public bool OpenWorldHint { get; }

   [JsonProperty("destructiveHint", NullValueHandling = NullValueHandling.Ignore)]
   public bool? DestructiveHint { get; }

   /// <summary>No current edit or project reload promises that repeating it has no further effect.</summary>
   [JsonProperty("idempotentHint", NullValueHandling = NullValueHandling.Ignore)]
   public bool? IdempotentHint => ReadOnlyHint ? null : false;
}
