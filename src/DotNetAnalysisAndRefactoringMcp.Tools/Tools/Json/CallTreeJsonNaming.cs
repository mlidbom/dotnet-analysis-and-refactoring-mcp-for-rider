#nullable enable
using DotNetAnalysisAndRefactoringMcp.CallTrees;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>
///   How the <see cref="JsonFormat">JSON form</see> of <c>get_call_hierarchy</c>'s trees words what the nodes of both
///   directions have, <see cref="CalleeJson"/> and <see cref="CallerJson"/>: a property's accessors, and why what is
///   under a node isn't listed again.
/// </summary>
public static class CallTreeJsonNaming
{
   /// <summary>"get", "set" or "get and set"; nothing for a function's <see cref="Accessors.None"/>.</summary>
   public static string? NameOf(Accessors accessors) =>
      accessors switch
      {
         Accessors.Get       => "get",
         Accessors.Set       => "set",
         Accessors.GetAndSet => "get and set",
         _                   => null
      };

   /// <summary>"listed above" or "recursive"; nothing for <see cref="Repetition.None"/>.</summary>
   public static string? NameOf(Repetition repetition) =>
      repetition switch
      {
         Repetition.ListedAbove => "listed above",
         Repetition.Recursive   => "recursive",
         _                      => null
      };
}
