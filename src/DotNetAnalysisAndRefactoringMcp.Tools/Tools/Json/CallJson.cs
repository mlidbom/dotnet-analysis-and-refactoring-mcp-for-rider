#nullable enable
using System.Collections.Generic;
using System.Linq;
using DotNetAnalysisAndRefactoringMcp.CallTrees;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>One node of <c>get_call_hierarchy</c>'s outgoing tree in the <see cref="JsonFormat">JSON form</see>: a member, and the calls under it.</summary>
public sealed class CallJson(
   DeclarationJson member,
   string? accessors,
   string? reachedAs,
   string? repetition,
   List<CallJson>? calls,
   List<CallJson>? overriders,
   int? overridersNotListed)
{
   [JsonProperty("member")] public DeclarationJson Member { get; } = member;

   /// <summary>For a property or an indexer, the accessors the call runs: "get", "set" or "get and set".</summary>
   [JsonProperty("accessors", NullValueHandling = NullValueHandling.Ignore)] public string? Accessors { get; } = accessors;

   /// <summary>
   ///   "delegate" for a method passed as a delegate, "implementation" or "override" for a member that the one above can
   ///   run instead; left out for a call.
   /// </summary>
   [JsonProperty("reachedAs", NullValueHandling = NullValueHandling.Ignore)] public string? ReachedAs { get; } = reachedAs;

   /// <summary>Why its calls aren't listed under it: "listed above", under an earlier node at least as deep, or "recursive".</summary>
   [JsonProperty("repetition", NullValueHandling = NullValueHandling.Ignore)] public string? Repetition { get; } = repetition;

   [JsonProperty("calls", NullValueHandling = NullValueHandling.Ignore)] public List<CallJson>? Calls { get; } = calls;

   /// <summary>The members that implement or override it, at the same depth: a call to it may run them instead.</summary>
   [JsonProperty("overriders", NullValueHandling = NullValueHandling.Ignore)] public List<CallJson>? Overriders { get; } = overriders;

   [JsonProperty("overridersNotListed", NullValueHandling = NullValueHandling.Ignore)] public int? OverridersNotListed { get; } = overridersNotListed;

   public static CallJson Of(CallTreeNode node) =>
      new(DeclarationJson.Of(node.Callee.Member),
          node.Callee.Accessors switch { CallTrees.Accessors.Get => "get", CallTrees.Accessors.Set => "set", CallTrees.Accessors.GetAndSet => "get and set", _ => null },
          node.Reached switch { HowReached.AsDelegate => "delegate", HowReached.Implementation => "implementation", HowReached.Override => "override", _ => null },
          node.Repetition switch { CallTrees.Repetition.ListedAbove => "listed above", CallTrees.Repetition.Recursive => "recursive", _ => null },
          node.Calls.Count > 0 ? [.. node.Calls.Select(Of)] : null,
          node.Overriders.Count > 0 ? [.. node.Overriders.Select(Of)] : null,
          node.OverridersNotListed > 0 ? node.OverridersNotListed : null);
}
