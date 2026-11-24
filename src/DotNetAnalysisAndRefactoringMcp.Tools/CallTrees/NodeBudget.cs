namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   How many more nodes a call tree may add: one budget for every tree of a call, so that it stays well inside the time
///   ReSharper lets a read lock be held, however wide the trees and however many overloads a name has.
/// </summary>
public sealed class NodeBudget
{
   public const int Nodes = 300;

   int _left = Nodes;

   /// <summary>Whether a node was refused: the tree is cut short.</summary>
   public bool Truncated { get; private set; }

   /// <summary>
   ///   Whether a node is left, asked before a costly search for more, such as for a member's callers. When none is,
   ///   the tree is cut short as surely as if the search's nodes had been refused.
   /// </summary>
   public bool HasNodesLeft()
   {
      if(_left > 0)
         return true;

      Truncated = true;
      return false;
   }

   /// <summary>Takes one node from the budget, or says there are none left.</summary>
   public bool TryTake()
   {
      if(!HasNodesLeft())
         return false;

      _left--;
      return true;
   }
}
