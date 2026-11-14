using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

public class When_asking_who_calls_an_implementation(TheSandboxWithTheFixtureSolution sandbox)
{
  readonly string _answer = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Circle.Area", direction = "incoming" });

  /// <summary>It found none until 2026-11-14: it followed no interface.</summary>
  [XF] public void it_finds_the_callers_that_call_it_through_the_interface() => _answer.Must().Contain("AreaOfTheFirst");

  [XF] public void and_says_which_interface_member_they_call() => _answer.Must().Contain("\"through\": \"Shapes.IShape.Area\"");

  [XF] public void it_finds_a_caller_that_calls_it_from_a_lambda() => _answer.Must().Contain("Shapes.ShapeExtensions.TotalArea");

  /// <summary>Until 2026-11-14 a caller had to be a method: a call from an expression-bodied property was dropped.</summary>
  [XF] public void it_finds_a_caller_that_is_a_property() => _answer.Must().Contain("AreaOfTheLast");

  [XF] public void and_the_callers_of_its_callers() => _answer.Must().Contain("ScaledAreaOfTheFirst");
}
