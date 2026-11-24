#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>
///   <c>get_call_hierarchy</c>'s incoming tree in the <see cref="JsonFormat">JSON form</see>: a tree for the member asked
///   about, or one for each overload of a member's name, and what they left out.
/// </summary>
public sealed class IncomingCallTreeJson(
   int maxDepth,
   string? namespaces,
   List<CallerJson> starts,
   int callersOutsideTheNamespaces,
   bool truncated)
{
   [JsonProperty("direction")] public string Direction => "incoming";

   [JsonProperty("maxDepth")] public int MaxDepth { get; } = maxDepth;

   /// <summary>The namespaces the callers were narrowed to, as the 'namespaces' argument gave them; left out when it gave none.</summary>
   [JsonProperty("namespaces", NullValueHandling = NullValueHandling.Ignore)] public string? Namespaces { get; } = namespaces;

   [JsonProperty("starts")] public List<CallerJson> Starts { get; } = starts;

   [JsonProperty("callersOutsideTheNamespaces")] public int CallersOutsideTheNamespaces { get; } = callersOutsideTheNamespaces;

   /// <summary>True when the trees stopped at the node budget, before the depth asked for.</summary>
   [JsonProperty("truncated")] public bool Truncated { get; } = truncated;
}
