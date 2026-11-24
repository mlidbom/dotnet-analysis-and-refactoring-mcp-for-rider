namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   What <c>get_call_hierarchy</c> answers with: an <see cref="OutgoingCallTree"/>, of the calls a member makes, or
///   an <see cref="IncomingCallTree"/>, of the code that calls it; one tree for each member asked about.
/// </summary>
public interface ICallTree
{
   /// <summary>Adds the tree that starts at <paramref name="start"/>.</summary>
   void Add(Callee start);

   /// <summary>The answer as an indented outline, paths relative to <paramref name="root"/>.</summary>
   string Text(SolutionRoot root);

   /// <summary>The answer in the JSON form, <c>format: "json"</c>.</summary>
   object Json();
}
