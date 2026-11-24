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

   public bool Exhausted => _left <= 0;

   /// <summary>Takes one node from the budget, or says there are none left.</summary>
   public bool TryTake()
   {
      if(_left <= 0)
      {
         Truncated = true;
         return false;
      }

      _left--;
      return true;
   }
}
