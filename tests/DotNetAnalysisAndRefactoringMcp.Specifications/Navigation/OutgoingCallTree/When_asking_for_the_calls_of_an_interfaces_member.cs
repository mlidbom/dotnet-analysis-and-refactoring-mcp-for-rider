using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.OutgoingCallTree;

/// <summary>IShape.Area has no code of its own: what a call to it runs is one of its implementations.</summary>
public class When_asking_for_the_calls_of_an_interfaces_member(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _calls = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Shapes.IShape.Area", direction = "outgoing" });

   [XF] public void its_implementations_are_listed_under_it() =>
      _calls.Must().EndWith("\nShapes.IShape.Area  Shapes\\IShape.cs:6\n"
                          + "  implementation: Shapes.Circle.Area  Shapes\\Circle.cs:6\n"
                          + "  implementation: Shapes.Square.Area  Shapes\\Square.cs:6");
}
