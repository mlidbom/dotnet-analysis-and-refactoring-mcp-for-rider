#nullable enable
using System.Collections.Generic;
using System.Text;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Impl;

namespace DotNetAnalysisAndRefactoringMcp.TypeHierarchies;

/// <summary>
///   A type's <see cref="Supertypes"/> or <see cref="Subtypes"/>, to a depth: what <c>get_type_hierarchy</c> answers, as
///   an indented outline of <c>kind Namespace.Type  path:line</c>, each type under the one it inherits from or that
///   inherits from it.
/// </summary>
/// <remarks>
///   A type met again, as an interface that two types of the tree implement, lists the types beyond it once.
///   <br/>Until 2026-11-25 the answer was JSON: each type's full path, its name twice, and an empty
///   <c>"children": []</c> at every leaf.
/// </remarks>
public abstract class TypeHierarchy
{
   /// <summary>The most types a hierarchy lists: <c>object</c>'s subtypes would be every class in the solution.</summary>
   public const int MostTypes = 500;

   readonly int _maxDepth;
   readonly TypeHierarchyNode _start;
   int _typesLeft = MostTypes;
   bool _truncated;

   /// <summary>For each type whose next level was looked for: its node, and how many levels were.</summary>
   readonly Dictionary<ITypeElement, (TypeHierarchyNode Node, int Levels)> _listed = new(DeclaredElementEqualityComparer.TypeElementComparer);

   /// <summary>The types the hierarchy steps over because ReSharper failed to read them, for the answer's last line.</summary>
   protected SkippedElements Skipped { get; } = new();

   protected TypeHierarchy(ITypeElement start, int maxDepth)
   {
      _maxDepth = maxDepth;
      _start = new TypeHierarchyNode(start, TypeHierarchyNode.NameOf(start));
   }

   /// <summary>The answer's heading when there are types to list: <c>Supertypes, 3 levels up</c>.</summary>
   protected abstract string Heading(string levels);

   /// <summary>The answer's heading when there are none: <c>No subtypes in the solution</c>.</summary>
   protected abstract string HeadingOfNone { get; }

   /// <summary>The types one level further from <paramref name="type"/>, up or down, as nodes to list under it.</summary>
   protected abstract IReadOnlyList<TypeHierarchyNode> NextLevelOf(ITypeElement type);

   /// <summary>Lists the levels under the start, to the depth.</summary>
   protected void Fill() => Fill(_start, depth: 0);

   /// <summary>
   ///   The answer's text: a heading saying how deep, the tree, and what was left out:
   ///   <code>
   ///   Subtypes in the solution, 3 levels down:
   ///
   ///   interface Shapes.Drawing.Renaming.IMeasured  Shapes.Drawing\Renaming\IMeasured.cs:4
   ///     abstract class Shapes.Drawing.Renaming.Segment  Shapes.Drawing\Renaming\Segment.cs:4
   ///       sealed class Shapes.Drawing.Renaming.StraightSegment  Shapes.Drawing\Renaming\StraightSegment.cs:4
   ///   </code>
   /// </summary>
   public string Text(SolutionRoot root)
   {
      var text = new StringBuilder(_start.TypesUnderIt.Count == 0
                                      ? $"{HeadingOfNone}:\n\n"
                                      : $"{Heading(_maxDepth == 1 ? "1 level" : $"{_maxDepth} levels")}:\n\n");
      _start.WriteTo(text, "", root);

      if(_truncated)
         text.Append($"\nStopped at {MostTypes} types: lower maxDepth.\n");
      if(Skipped.Line is { } skipped)
         text.Append($"\n{skipped}\n");

      return text.ToString().TrimEnd();
   }

   void Fill(TypeHierarchyNode node, int depth)
   {
      var levelsLeft = _maxDepth - depth;
      if(levelsLeft <= 0)
         return;

      if(_listed.TryGetValue(node.Type, out var listed) && listed.Levels >= levelsLeft)
      {
         node.IsListedAbove = listed.Node.TypesUnderIt.Count > 0;
         return;
      }

      _listed[node.Type] = (node, levelsLeft);
      foreach(var next in NextLevelOf(node.Type))
      {
         if(!TryTakeAType())
            break;
         node.TypesUnderIt.Add(next);
      }

      foreach(var next in node.TypesUnderIt)
         Fill(next, depth + 1);
   }

   bool TryTakeAType()
   {
      if(_typesLeft == 0)
      {
         _truncated = true;
         return false;
      }

      _typesLeft--;
      return true;
   }
}
