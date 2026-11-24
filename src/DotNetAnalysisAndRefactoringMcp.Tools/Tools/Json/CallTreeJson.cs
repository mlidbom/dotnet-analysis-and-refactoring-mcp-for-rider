#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>
///   <c>get_call_hierarchy</c>'s outgoing tree in the <see cref="JsonFormat">JSON form</see>: a tree for the member asked
///   about, or one for each overload of a member's name, and what they left out.
/// </summary>
public sealed class CallTreeJson(
   int maxDepth,
   string scope,
   List<CallJson> starts,
   IReadOnlyDictionary<string, int> callsIntoAssembliesNotShown,
   int callsOutsideTheNamespaces,
   bool truncated)
{
   [JsonProperty("direction")] public string Direction => "outgoing";

   [JsonProperty("maxDepth")] public int MaxDepth { get; } = maxDepth;

   /// <summary>The code the calls were followed into, as the text form's heading says it.</summary>
   [JsonProperty("scope")] public string Scope { get; } = scope;

   [JsonProperty("starts")] public List<CallJson> Starts { get; } = starts;

   /// <summary>Calls into compiled assemblies outside the scope, by the assembly's name, which the 'assemblies' argument takes.</summary>
   [JsonProperty("callsIntoAssembliesNotShown")] public IReadOnlyDictionary<string, int> CallsIntoAssembliesNotShown { get; } = callsIntoAssembliesNotShown;

   [JsonProperty("callsOutsideTheNamespaces")] public int CallsOutsideTheNamespaces { get; } = callsOutsideTheNamespaces;

   /// <summary>True when the trees stopped at the node budget, before the depth asked for.</summary>
   [JsonProperty("truncated")] public bool Truncated { get; } = truncated;
}
