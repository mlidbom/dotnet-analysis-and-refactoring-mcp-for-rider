#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Impl;

namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   One node of an <see cref="IncomingCallTree"/>: a <see cref="CallTrees.Caller"/>, the lines on which its code calls
///   the node above, and the callers of its own code under it.
/// </summary>
public sealed class IncomingCallTreeNode
{
   const int MostLinesListed = 5;

   public Caller Caller { get; }

   /// <summary>The calls with which its code calls the node above, in the order written; none for the start of the tree.</summary>
   public IReadOnlyList<CallSite> CallSites { get; }

   /// <summary>Whether it is where the tree starts: the member asked about.</summary>
   public bool IsTheStart { get; }

   public Repetition Repetition { get; set; }

   /// <summary>The callers of its code, in the order of their first call's file and line.</summary>
   public List<IncomingCallTreeNode> Callers { get; } = [];

   IncomingCallTreeNode(Caller caller, IReadOnlyList<CallSite> callSites, bool isTheStart)
   {
      Caller = caller;
      CallSites = callSites;
      IsTheStart = isTheStart;
   }

   /// <summary>The start of a tree: the member asked about, whose callers it lists.</summary>
   public static IncomingCallTreeNode StartingAt(Callee callee) => new(Caller.Of(callee), [], isTheStart: true);

   /// <summary>A node for <paramref name="caller"/>, which calls the node above with <paramref name="callSites"/>.</summary>
   public static IncomingCallTreeNode Calling(Caller caller, IReadOnlyList<CallSite> callSites) => new(caller, callSites, isTheStart: false);

   /// <summary>
   ///   Whether the tree goes on to its callers: a caller written in a field's initializer, say, is called by nothing
   ///   in particular.
   /// </summary>
   public bool CallersCanBeFollowed => Caller.Callee != null;

   /// <summary>
   ///   Its line, <c>Shapes.Drawing.Drawing.AreaOfTheFirst  Shapes.Drawing\Drawing.cs:12  (through Shapes.IShape.Area)</c>,
   ///   after <paramref name="indentation"/>; the lines on which it calls the node above, as <c>find_usages</c> shows
   ///   them, <c>12: public double AreaOfTheFirst() =&gt; _shapes[0].Area();</c>; and the nodes of its callers; each
   ///   indented two spaces more.
   /// </summary>
   public void WriteTo(StringBuilder text, string indentation, SolutionRoot root)
   {
      var declarationSite = Caller.DeclarationSite;
      text.Append(indentation).Append(Caller.Name).Append("  ").Append(PsiHelpers.Location(Caller.Member, root)).Append(Suffix).Append('\n');
      WriteTheLinesThatCallTheNodeAbove(text, indentation + "  ", declarationSite, root);
      foreach(var caller in Callers)
         caller.WriteTo(text, indentation + "  ", root);
   }

   /// <summary>
   ///   The first <see cref="MostLinesListed"/> lines on which it calls the node above, <c>12: public double
   ///   AreaOfTheFirst() =&gt; _shapes[0].Area();</c>, each with its <see cref="CallSite.FileAsWritten">file</see> when it
   ///   isn't the file of <paramref name="declarationSite"/>, and how many more there are.
   /// </summary>
   void WriteTheLinesThatCallTheNodeAbove(StringBuilder text, string indentation, DeclarationSite? declarationSite, SolutionRoot root)
   {
      var firstCallOnEachLine = CallSites.GroupBy(it => (it.File, it.GeneratedFile?.Name, it.Line)).Select(it => it.First()).ToList();
      foreach(var call in firstCallOnEachLine.Take(MostLinesListed))
         text.Append(indentation).Append(FileOf(call)).Append(call.Line).Append(": ").Append(call.Code).Append('\n');
      if(firstCallOnEachLine.Count > MostLinesListed)
         text.Append(indentation).Append("… and ").Append(firstCallOnEachLine.Count - MostLinesListed).Append(" more lines that call it\n");

      string FileOf(CallSite call) => call.IsInTheFileOf(declarationSite) ? "" : call.FileAsWritten(root) + ":";
   }

   string Suffix =>
      (IsTheStart ? WhatItsCallsAreSearchedThrough : HowItCallsTheNodeAbove)
    + (CallersCanBeFollowed ? "" : "  (in no method's or property's code: its callers aren't followed)")
    + Repetition switch
      {
         Repetition.ListedAbove => "  (its callers are listed above)",
         Repetition.Recursive   => "  (recursive)",
         _                      => ""
      };

   /// <summary>For the start: the members it overrides or implements, whose calls were searched too, and those that weren't.</summary>
   string WhatItsCallsAreSearchedThrough => SuperMembers.Of(Caller.Member).Line("calls") is { } line ? "  " + line : "";

   string HowItCallsTheNodeAbove => WhatItCallsTheNodeAboveThrough + WhetherItPassesTheNodeAboveAsADelegate;

   /// <summary>
   ///   <c>  (through Shapes.IShape.Area)</c> when it calls the node above through members that node overrides or
   ///   implements, <c>  (directly, and through Shapes.IShape.Area)</c> when by its own name too; nothing when only by
   ///   its own name.
   /// </summary>
   string WhatItCallsTheNodeAboveThrough
   {
      get
      {
         var through = NamesOfTheMembersCalledThrough;
         return through.Count == 0                         ? ""
              : CallSites.Any(it => it.Through == null) ? $"  (directly, and through {string.Join(", ", through)})"
                                                        : $"  (through {string.Join(", ", through)})";
      }
   }

   /// <summary>The qualified names of the members, each once, that it calls the node above through: <see cref="CallSite.Through"/>.</summary>
   IReadOnlyList<string> NamesOfTheMembersCalledThrough =>
      [.. CallSites.Select(it => it.Through).OfType<IDeclaredElement>().Distinct(DeclaredElementEqualityComparer.ElementComparer).Select(PsiHelpers.GetQualifiedName)];

   /// <summary>
   ///   <c>  (passes it as a delegate)</c> when it only passes the node above as a delegate,
   ///   <c>  (calls it, and passes it as a delegate)</c> when it also calls it; nothing when it only calls it.
   /// </summary>
   string WhetherItPassesTheNodeAboveAsADelegate =>
      !CallSites.Any(it => it.AsDelegate)  ? ""
    : CallSites.All(it => it.AsDelegate) ? "  (passes it as a delegate)"
                                           : "  (calls it, and passes it as a delegate)";
}
