namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>One call that a <see cref="CallTrees.Callee"/>'s code makes, as it is written there.</summary>
public sealed class Call(Callee callee, bool asDelegate, bool isThroughBase)
{
   public Callee Callee { get; } = callee;

   /// <summary>
   ///   How the code that makes it reaches its <see cref="Callee"/>: <see cref="HowReached.AsDelegate"/> for a method
   ///   passed as a delegate, such as <c>shapes.Select(AreaOf)</c>, which runs when the method it is passed to calls it,
   ///   not where it is written; otherwise <see cref="HowReached.Call"/>.
   /// </summary>
   public HowReached Reached { get; } = asDelegate ? HowReached.AsDelegate : HowReached.Call;

   /// <summary>
   ///   Whether it is written <c>base.Describe()</c>, which runs the member itself, never an override of it.
   /// </summary>
   public bool IsThroughBase { get; } = isThroughBase;
}
