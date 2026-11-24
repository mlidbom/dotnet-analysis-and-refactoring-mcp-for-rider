using System;

namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>Which of a property's or an indexer's accessors a call runs: reading it runs the getter, writing it the setter, <c>+=</c> both.</summary>
[Flags]
public enum Accessors
{
   /// <summary>No accessor: the callee is a method, a constructor, an operator or a local function.</summary>
   None = 0,
   Get = 1,

   /// <summary>The setter, or the <c>init</c> accessor.</summary>
   Set = 2,
   GetAndSet = Get | Set
}
