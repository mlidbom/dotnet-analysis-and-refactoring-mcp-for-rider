#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DotNetAnalysisAndRefactoringMcp.Tools.Json;

namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   The calls a member's code makes, and the calls theirs make, to a depth, within a <see cref="CallScope"/>: what
///   <c>get_call_hierarchy</c> answers for <c>direction: "outgoing"</c>, as an indented outline of
///   <c>Type.Member  path:line</c>. A call to a member that can be overridden lists its implementations and overrides
///   under it, at the same depth, since the call may run any of them.
/// </summary>
/// <remarks>
///   Each callee's calls are listed once, where it first comes; a later node of it says they are listed above, unless
///   it is shallower and lists them deeper. Until 2026-11-24 the tree followed calls only through a name, stopped at an
///   interface or a virtual member, listed every call into compiled code, and repeated a callee's whole subtree at every
///   node of it, in JSON: for <c>GetSymbolSourceTool.Execute</c> at depth 2, 48 nodes in 16,912 characters.
/// </remarks>
public sealed class OutgoingCallTree(CallScope scope, int maxDepth, NodeBudget budget)
{
   const int MostOverridersListed = 10;

   readonly CallScope _scope = scope;
   readonly int _maxDepth = maxDepth;
   readonly NodeBudget _budget = budget;
   readonly CallsNotShown _notShown = new(scope);
   readonly List<CallTreeNode> _starts = [];

   /// <summary>For each callee with something listed under it, its calls or its overriders: how many levels of its calls.</summary>
   readonly Dictionary<Callee, int> _levelsListed = new();

   /// <summary>Adds the tree of the calls <paramref name="start"/> makes, and of its implementations or overrides.</summary>
   public void Add(Callee start)
   {
      var node = new CallTreeNode(start, HowReached.Start);
      Fill(node, depth: 0, callers: [], mayListOverriders: true);
      _starts.Add(node);
   }

   /// <summary>
   ///   The answer's text: a heading saying how deep and into what, each tree, and what was left out:
   ///   <code>
   ///   Calls into the solution's code, 2 levels deep:
   ///
   ///   Shapes.Drawing.Sketch.Draw  Shapes.Drawing\Sketch.cs:19
   ///     Shapes.Drawing.Sketch.Caption  Shapes.Drawing\Sketch.cs:30
   ///       Shapes.Palette.Mix(first:string, second:string)  Shapes\Palette.Mixing.cs:5
   ///   </code>
   /// </summary>
   public string Text(SolutionRoot root)
   {
      var text = new StringBuilder($"Calls into {_scope.Description}, {_maxDepth} {(_maxDepth == 1 ? "level" : "levels")} deep:\n");
      foreach(var start in _starts)
      {
         text.Append('\n');
         start.WriteTo(text, "", root);
      }

      if(_notShown.Line() is { } notShown)
         text.Append('\n').Append(notShown).Append('\n');
      if(_budget.Truncated)
         text.Append($"Stopped at {NodeBudget.Nodes} nodes: lower maxDepth, or narrow the scope with 'namespaces'.\n");

      return text.ToString().TrimEnd();
   }

   public CallTreeJson Json() =>
      new(_maxDepth,
          _scope.Description,
          [.. _starts.Select(CallJson.Of)],
          _notShown.IntoAssemblies,
          _notShown.OutsideTheNamespaces,
          _budget.Truncated);

   /// <summary>
   ///   Lists under <paramref name="node"/>, <paramref name="depth"/> levels below the start, the calls its code makes
   ///   and, when <paramref name="mayListOverriders"/>, its implementations or overrides; or marks it as a
   ///   <see cref="CallTrees.Repetition"/>. <paramref name="callers"/> are the callees whose code runs on the way to it,
   ///   for recursion. A leaf, with nothing listed under it, is no reason to mark a later node of the same callee.
   /// </summary>
   void Fill(CallTreeNode node, int depth, IReadOnlyList<Callee> callers, bool mayListOverriders)
   {
      var levelsLeft = _maxDepth - depth;
      var listsCalls = levelsLeft > 0;
      var listsOverriders = mayListOverriders && node.Callee.CanBeOverridden;
      if(callers.Contains(node.Callee))
         node.Repetition = Repetition.Recursive;
      else if(IsListedAbove())
         node.Repetition = Repetition.ListedAbove;
      else if(listsCalls || listsOverriders)
      {
         _levelsListed[node.Callee] = levelsLeft;
         if(listsCalls)
            ListCalls(node, depth, [.. callers, node.Callee]);
         if(listsOverriders)
            ListOverriders(node, depth, callers);
      }

      bool IsListedAbove() => _levelsListed.TryGetValue(node.Callee, out var levelsListed) && levelsListed >= levelsLeft;
   }

   void ListCalls(CallTreeNode node, int depth, IReadOnlyList<Callee> callers)
   {
      foreach(var call in CallsInCode.Of(node.Callee))
      {
         if(!_scope.Contains(call.Callee))
         {
            _notShown.Add(call.Callee);
            continue;
         }

         if(!_budget.TryTake())
            return;

         var calleeNode = new CallTreeNode(call.Callee, call.Reached);
         node.Calls.Add(calleeNode);
         Fill(calleeNode, depth + 1, callers, mayListOverriders: !call.IsThroughBase);
      }
   }

   /// <summary>
   ///   An overrider is at the depth of the member it overrides, reached by the same callers: the call runs it instead.
   ///   Its own overriders are among the member's, so they aren't searched for again. In the order of their names, which
   ///   ReSharper's search doesn't keep from one call to the next.
   /// </summary>
   void ListOverriders(CallTreeNode node, int depth, IReadOnlyList<Callee> callers)
   {
      var overriders = node.Callee.Overriders().Where(_scope.Contains).OrderBy(it => it.Name, StringComparer.Ordinal).ToList();
      var reached = node.Callee.IsAnInterfacesMember ? HowReached.Implementation : HowReached.Override;
      foreach(var overrider in overriders.Take(MostOverridersListed))
      {
         if(!_budget.TryTake())
            break;

         var overriderNode = new CallTreeNode(overrider, reached);
         node.Overriders.Add(overriderNode);
         Fill(overriderNode, depth, callers, mayListOverriders: false);
      }

      node.OverridersNotListed = overriders.Count - node.Overriders.Count;
   }
}
