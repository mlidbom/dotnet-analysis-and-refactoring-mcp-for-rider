#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DotNetAnalysisAndRefactoringMcp.Tools.Json;
using JetBrains.ProjectModel;

namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   The code that calls a member, and the code that calls that, to a depth: what <c>get_call_hierarchy</c> answers for
///   <c>direction: "incoming"</c>, as an indented outline of <c>Type.Member  path:line</c>, each caller with the lines
///   on which it calls the member above it, as <c>find_usages</c> shows them, so that what it passes is seen without
///   opening the file. A call through a member that the one above overrides or implements, such as an interface's,
///   is a call to it too, and says so.
/// </summary>
/// <remarks>
///   The calls are those the outgoing tree follows, <see cref="CallsInCode"/>, found by ReSharper's search for
///   references (<see cref="CodeThatCalls"/>): not a doc comment's or a <c>nameof</c>'s, and for a property, only
///   those through the accessors the node stands for, so that a getter's callers are the code that reads the property.
///   A caller met again lists its callers once, as in the <see cref="OutgoingCallTree"/>. A call in no member, such as
///   in a top-level statement, isn't listed.
///   <br/>Until 2026-11-25 the tree was JSON, each file's full path at every node, with no line of code, and took
///   a doc comment's <c>cref</c> for a call.
/// </remarks>
public sealed class IncomingCallTree(ISolution solution, CallScope scope, int maxDepth, NodeBudget budget) : ICallTree
{
   readonly ISolution _solution = solution;
   readonly CallScope _scope = scope;
   readonly int _maxDepth = maxDepth;
   readonly NodeBudget _budget = budget;
   readonly List<IncomingCallTreeNode> _starts = [];
   int _callersOutsideTheNamespaces;

   /// <summary>For each caller with its callers listed under it: how many levels of them.</summary>
   readonly Dictionary<Caller, int> _levelsListed = new();

   /// <summary>Adds the tree of the callers of <paramref name="start"/>.</summary>
   public void Add(Callee start)
   {
      var node = IncomingCallTreeNode.StartingAt(start);
      Fill(node, depth: 0, callersAbove: []);
      _starts.Add(node);
   }

   /// <summary>
   ///   The answer's text: a heading saying how deep, each tree, and what was left out:
   ///   <code>
   ///   Callers, 2 levels deep:
   ///
   ///   Shapes.Circle.Area  Shapes\Circle.cs:6  (includes calls through Shapes.IShape.Area)
   ///     Shapes.Drawing.Drawing.AreaOfTheFirst  Shapes.Drawing\Drawing.cs:12  (through Shapes.IShape.Area)
   ///       12: public double AreaOfTheFirst() => _shapes[0].Area();
   ///   </code>
   /// </summary>
   public string Text(SolutionRoot root)
   {
      var text = new StringBuilder($"Callers{(_scope.IsNarrowed ? $" in {_scope.Namespaces}" : "")}, {_maxDepth} {(_maxDepth == 1 ? "level" : "levels")} deep:\n");
      foreach(var start in _starts)
      {
         text.Append('\n');
         start.WriteTo(text, "", root);
      }

      if(_callersOutsideTheNamespaces > 0)
         text.Append($"\nNot shown: {_callersOutsideTheNamespaces} {(_callersOutsideTheNamespaces == 1 ? "caller" : "callers")} outside {_scope.Namespaces}.\n");
      if(_budget.Truncated)
         text.Append($"Stopped at {NodeBudget.Nodes} nodes: lower maxDepth, or narrow the scope with 'namespaces'.\n");

      return text.ToString().TrimEnd();
   }

   public IncomingCallTreeJson Json() =>
      new(_maxDepth,
          _scope.IsNarrowed ? _scope.Namespaces : null,
          [.. _starts.Select(CallerJson.Of)],
          _callersOutsideTheNamespaces,
          _budget.Truncated);

   object ICallTree.Json() => Json();

   /// <summary>
   ///   Lists under <paramref name="node"/>, <paramref name="depth"/> levels below the start, its callers, or marks it
   ///   as a <see cref="CallTrees.Repetition"/>. <paramref name="callersAbove"/> are the
   ///   <see cref="IncomingCallTreeNode.Caller"/>s of the nodes above it, from the start down, for recursion.
   /// </summary>
   void Fill(IncomingCallTreeNode node, int depth, IReadOnlyList<Caller> callersAbove)
   {
      if(node.Caller.Callee is not { } callee)
         return;

      var levelsLeft = _maxDepth - depth;
      if(callersAbove.Contains(node.Caller))
         node.Repetition = Repetition.Recursive;
      else if(IsListedAbove())
         node.Repetition = Repetition.ListedAbove;
      else if(levelsLeft > 0 && _budget.HasNodesLeft())
      {
         _levelsListed[node.Caller] = levelsLeft;
         ListCallers(node, callee, depth, [.. callersAbove, node.Caller]);
      }

      bool IsListedAbove() => _levelsListed.TryGetValue(node.Caller, out var levelsListed) && levelsListed >= levelsLeft;
   }

   /// <summary>Every caller of the node at this level before the callers of any of them: the nearest callers matter most.</summary>
   void ListCallers(IncomingCallTreeNode node, Callee callee, int depth, IReadOnlyList<Caller> callersAbove)
   {
      foreach(var (caller, callSites) in CodeThatCalls.Of(callee, _solution))
      {
         if(!_scope.Contains(caller))
         {
            _callersOutsideTheNamespaces++;
            continue;
         }

         if(!_budget.TryTake())
            break;

         node.Callers.Add(IncomingCallTreeNode.Calling(caller, callSites));
      }

      foreach(var callerNode in node.Callers)
         Fill(callerNode, depth + 1, callersAbove);
   }
}
