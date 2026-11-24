#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Application.Progress;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Search;

namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   The code that calls a <see cref="Callee"/>, by its own name or through a member it overrides or implements, as
///   ReSharper's search for references finds it: each <see cref="Caller"/> with the <see cref="CallSite"/>s of its calls.
///   What an <see cref="IncomingCallTree"/> lists under a node, as <see cref="CallsInCode"/> is what an
///   <see cref="OutgoingCallTree"/> lists.
/// </summary>
/// <remarks>
///   A reference is a call when <see cref="CallsInCode"/> would find it in the code it is written in, so not a doc
///   comment's <c>cref</c> nor a <c>nameof</c>; for a property, only when it runs an accessor the callee stands for.
///   A <c>base.Describe()</c> names the member it overrides but runs that member, never the callee.
/// </remarks>
public static class CodeThatCalls
{
   /// <summary>Each caller of <paramref name="callee"/> with its calls in the order written, the callers in the order of their first call's file and line.</summary>
   public static IEnumerable<(Caller Caller, IReadOnlyList<CallSite> CallSites)> Of(Callee callee, ISolution solution)
   {
      var callsFound = new List<(Caller Caller, CallSite CallSite)>();
      var searchDomain = SearchDomainFactory.Instance.CreateSearchDomain(solution, false);
      FindCallsTo(callee.Member, through: null);
      foreach(var superMember in SuperMembers.Of(callee.Member).InSource)
         FindCallsTo(superMember, through: superMember);

      return callsFound.GroupBy(it => it.Caller)
                       .Select(it => (Caller: it.Key, CallSites: (IReadOnlyList<CallSite>)[.. InOrder(it.Select(found => found.CallSite))]))
                       .OrderBy(it => it.CallSites[0].File, StringComparer.OrdinalIgnoreCase)
                       .ThenBy(it => it.CallSites[0].Line);

      void FindCallsTo(IDeclaredElement member, IDeclaredElement? through) =>
         solution.GetPsiServices().Finder.FindReferences(
            member,
            searchDomain,
            new FindResultConsumer(result =>
            {
               if(result is FindResultReference { Reference: var reference }
               && CallsInCode.MadeThrough(reference) is { } call
               && RunsTheCallee(call, through)
               && Caller.Around(reference.GetTreeNode()) is { } caller
               && CallSite.Of(reference, call, through) is { } callSite)
                  callsFound.Add((caller, callSite));
               return FindExecution.Continue;
            }),
            NullProgressIndicator.Create());

      bool RunsTheCallee(Call call, IDeclaredElement? through) => callee.RunsWhen(call.Callee) && !(through != null && call.IsThroughBase);
   }

   static IEnumerable<CallSite> InOrder(IEnumerable<CallSite> callSites) =>
      callSites.OrderBy(it => it.File, StringComparer.OrdinalIgnoreCase).ThenBy(it => it.Line).ThenBy(it => it.Column);
}
