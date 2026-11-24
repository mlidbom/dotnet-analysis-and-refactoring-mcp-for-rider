namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>Why a <see cref="CallTreeNode"/>'s calls aren't listed under it again, when they aren't.</summary>
public enum Repetition
{
   /// <summary>Its calls are listed under it, if the depth allows.</summary>
   None,

   /// <summary>Its calls are listed under an earlier node of the same callee, at least as deep.</summary>
   ListedAbove,

   /// <summary>It calls itself: the same callee is above it on the way from the start.</summary>
   Recursive
}
