#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   One node of a call tree: a <see cref="CallTrees.Callee"/>, how the node above reaches it, the calls its code makes,
///   and, for a member that can be overridden, the members that implement or override it.
/// </summary>
public sealed class CallTreeNode(Callee callee, HowReached reached)
{
   public Callee Callee { get; } = callee;

   public HowReached Reached { get; } = reached;

   public Repetition Repetition { get; set; }

   /// <summary>The calls its code makes, in the order written.</summary>
   public List<CallTreeNode> Calls { get; } = [];

   /// <summary>The members that implement or override it, which a call to it may run instead, at the same depth as it.</summary>
   public List<CallTreeNode> Overriders { get; } = [];

   /// <summary>How many more members implement or override it than <see cref="Overriders"/> lists.</summary>
   public int OverridersNotListed { get; set; }

   /// <summary>
   ///   Its line, <c>Shapes.Sketch.AreaOf  Shapes\Sketch.cs:31  (passed as a delegate)</c>, after
   ///   <paramref name="indentation"/>, and the lines of the nodes under it, indented two spaces more.
   /// </summary>
   public void WriteTo(StringBuilder text, string indentation, SolutionRoot root)
   {
      text.Append(indentation).Append(Prefix).Append(Callee.Name).Append("  ").Append(Callee.Location(root)).Append(Suffix).Append('\n');
      foreach(var node in Calls.Concat(Overriders))
         node.WriteTo(text, indentation + "  ", root);

      if(OverridersNotListed > 0)
         text.Append(indentation).Append("  … and ").Append(OverridersNotListed).Append(Callee.IsAnInterfacesMember ? " more implementations" : " more overrides")
             .Append(": find_implementations lists them all\n");
   }

   string Prefix =>
      Reached switch
      {
         HowReached.Implementation => "implementation: ",
         HowReached.Override       => "override: ",
         _                         => ""
      };

   string Suffix =>
      (Reached == HowReached.AsDelegate ? "  (passed as a delegate)" : "")
    + (IsAStartWhoseCallsAreUnknown ? "  (no C# source of its own, so its calls aren't known)" : "")
    + Repetition switch
      {
         Repetition.ListedAbove => "  (its calls are listed above)",
         Repetition.Recursive   => "  (recursive)",
         _                      => ""
      };

   /// <summary>The start of a tree with no code to find calls in, such as a compiled or a VB member, nor overriders that have some.</summary>
   bool IsAStartWhoseCallsAreUnknown => Reached == HowReached.Start && Callee.Code.Count == 0 && Overriders.Count == 0;
}
