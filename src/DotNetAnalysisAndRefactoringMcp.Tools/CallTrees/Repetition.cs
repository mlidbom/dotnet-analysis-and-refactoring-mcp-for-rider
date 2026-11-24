namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   Why the nodes under a call tree's node aren't listed again, when they aren't: the calls under an
///   <see cref="OutgoingCallTreeNode"/>, the callers under an <see cref="IncomingCallTreeNode"/>.
/// </summary>
public enum Repetition
{
   /// <summary>Its calls or callers are listed under it, if the depth allows.</summary>
   None,

   /// <summary>Its calls or callers are listed under an earlier node of the same member, at least as deep.</summary>
   ListedAbove,

   /// <summary>It calls itself: the same member is on the way from the start to it.</summary>
   Recursive
}
