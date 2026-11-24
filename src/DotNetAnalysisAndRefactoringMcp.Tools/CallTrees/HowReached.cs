namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>How a <see cref="CallTreeNode"/> is reached from the node above it.</summary>
public enum HowReached
{
   /// <summary>It is where the tree starts: the member asked about.</summary>
   Start,

   /// <summary>The code above calls it.</summary>
   Call,

   /// <summary>The code above passes it as a delegate, to be called by what it is passed to.</summary>
   AsDelegate,

   /// <summary>It implements the interface member above: a call to that runs it, when the object is of its type.</summary>
   Implementation,

   /// <summary>It overrides the member above: a call to that runs it, when the object is of its type.</summary>
   Override
}
