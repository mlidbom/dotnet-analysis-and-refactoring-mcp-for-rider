#nullable enable
using System.Collections.Generic;
using System.Linq;
using DotNetAnalysisAndRefactoringMcp.CallTrees;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>
///   One node of <c>get_call_hierarchy</c>'s incoming tree in the <see cref="JsonFormat">JSON form</see>: a caller, the
///   lines on which it calls the node above, and its own callers.
/// </summary>
public sealed class CallerJson(
   DeclarationJson member,
   string? accessors,
   List<CallSiteJson>? callSites,
   string? note,
   bool? callersNotFollowed,
   string? repetition,
   List<CallerJson>? callers)
{
   /// <summary>The member the calls are written in.</summary>
   [JsonProperty("member")] public DeclarationJson Member { get; } = member;

   /// <summary>For a property's code, the accessor it is in: "get" or "set"; for the start, every accessor it has.</summary>
   [JsonProperty("accessors", NullValueHandling = NullValueHandling.Ignore)] public string? Accessors { get; } = accessors;

   /// <summary>The calls with which it calls the node above, in the order written; left out for the start.</summary>
   [JsonProperty("callSites", NullValueHandling = NullValueHandling.Ignore)] public List<CallSiteJson>? CallSites { get; } = callSites;

   /// <summary>For the start, the members it overrides or implements whose calls were searched too, and those that weren't.</summary>
   [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)] public string? Note { get; } = note;

   /// <summary>True for calls in no method's or property's code, such as a field's initializer, whose callers aren't followed.</summary>
   [JsonProperty("callersNotFollowed", NullValueHandling = NullValueHandling.Ignore)] public bool? CallersNotFollowed { get; } = callersNotFollowed;

   /// <summary>Why its callers aren't listed under it: "listed above", under an earlier node at least as deep, or "recursive".</summary>
   [JsonProperty("repetition", NullValueHandling = NullValueHandling.Ignore)] public string? Repetition { get; } = repetition;

   [JsonProperty("callers", NullValueHandling = NullValueHandling.Ignore)] public List<CallerJson>? Callers { get; } = callers;

   public static CallerJson Of(IncomingCallTreeNode node) =>
      new(DeclarationJson.Of(node.Caller.Member),
          node.Caller.Callee is { } callee ? CallTreeJsonNaming.NameOf(callee.Accessors) : null,
          node.IsTheStart ? null : [.. node.CallSites.Select(CallSiteJson.Of)],
          node.IsTheStart ? SuperMembers.Of(node.Caller.Member).Line("calls") : null,
          node.CallersCanBeFollowed ? null : true,
          CallTreeJsonNaming.NameOf(node.Repetition),
          node.Callers.Count > 0 ? [.. node.Callers.Select(Of)] : null);
}
