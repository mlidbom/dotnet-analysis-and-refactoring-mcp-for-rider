using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   Palette.Mix has two overloads: the three-color one calls the two-color one, which calls Mixer.Of. Overloads share a
///   qualified name, which until 2026-11-24 the call hierarchy took for one member: a name gave the tree of whichever
///   overload ReSharper listed first, and a call from one overload into the other was not followed, as if it were a
///   cycle. Both overloads are expression-bodied, whose calls the outgoing tree missed until the same day.
/// </summary>
public class When_asking_for_the_call_hierarchy_of_an_overloaded_method(TheSandboxWithTheFixtureSolution sandbox)
{
   const string TheTwoColorOverload = "\"signature\": \"method Mix(first:string, second:string) : string\"";
   const string TheThreeColorOverload = "\"signature\": \"method Mix(first:string, second:string, third:string) : string\"";

   readonly string _callersByName = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Shapes.Palette.Mix", direction = "incoming" });
   readonly string _callersOfTheTwoColorOverload = sandbox.McpServer.CallTool("get_call_hierarchy", new { filePath = sandbox.PathOf("Shapes/Palette.Mixing.cs"), line = 5, column = 17, direction = "incoming" });
   readonly string _calleesOfTheThreeColorOverload = sandbox.McpServer.CallTool("get_call_hierarchy", new { filePath = sandbox.PathOf("Shapes/Palette.Mixing.cs"), line = 7, column = 17, direction = "outgoing" });

   [XF] public void by_name_it_gives_a_tree_for_every_overload() =>
      _callersByName.Must().Satisfy(it => it.Contains(TheTwoColorOverload) && it.Contains(TheThreeColorOverload));

   [XF] public void a_caller_that_is_an_overload_says_which_one() =>
      _callersOfTheTwoColorOverload.Must().Contain(TheThreeColorOverload);

   [XF] public void the_calls_an_expression_bodied_method_makes_are_its_callees_named_by_their_parameters() =>
      _calleesOfTheThreeColorOverload.Must().Contain("\n  Shapes.Palette.Mix(first:string, second:string)  Shapes\\Palette.Mixing.cs:5\n");

   [XF] public void a_call_into_another_overload_is_followed_into_it() =>
      _calleesOfTheThreeColorOverload.Must().Contain("\n    Shapes.Palette.Mixer.Of  Shapes\\Palette.Mixing.cs:14");
}
