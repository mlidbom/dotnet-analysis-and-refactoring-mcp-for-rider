using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.IncomingCallTree;

/// <summary>
///   Circle.Area implements IShape.Area, which code calls on shapes of any kind. Until 2026-11-25 the tree was JSON,
///   with no line of code at any caller.
/// </summary>
public class When_asking_who_calls_an_implementation(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Circle.Area", direction = "incoming" });

   [XF] public void it_starts_at_the_implementation_saying_that_calls_through_the_interface_are_included() =>
      _answer.Must().Contain("\nShapes.Circle.Area  Shapes\\Circle.cs:6  (includes calls through Shapes.IShape.Area)\n");

   /// <summary>It found none until 2026-11-14: it followed no interface.</summary>
   [XF] public void it_lists_a_caller_that_calls_it_through_the_interface_with_the_line_of_the_call() =>
      _answer.Must().Contain("\n  Shapes.Drawing.Drawing.AreaOfTheFirst  Shapes.Drawing\\Drawing.cs:12  (through Shapes.IShape.Area)\n    12: public double AreaOfTheFirst() => _shapes[0].Area();\n");

   [XF] public void it_lists_a_caller_that_calls_it_from_a_lambda() =>
      _answer.Must().Contain("\n  Shapes.ShapeExtensions.TotalArea  Shapes\\ShapeExtensions.cs:6  (through Shapes.IShape.Area)\n    6: public static double TotalArea(this IEnumerable<IShape> shapes) => shapes.Sum(it => it.Area());\n");

   /// <summary>Until 2026-11-14 a caller had to be a method: a call from an expression-bodied property was dropped.</summary>
   [XF] public void it_lists_a_caller_that_is_a_property() =>
      _answer.Must().Contain("\n  Shapes.Drawing.Drawing.AreaOfTheLast  Shapes.Drawing\\Drawing.cs:14  (through Shapes.IShape.Area)\n    14: public double AreaOfTheLast => _shapes[^1].Area();\n");

   [XF] public void it_lists_the_callers_of_its_callers_indented_under_them() =>
      _answer.Must().Contain("\n    Shapes.Drawing.Drawing.ScaledAreaOfTheFirst  Shapes.Drawing\\Drawing.cs:16\n      16: public double ScaledAreaOfTheFirst() => ShapeExtensions.Scaled(AreaOfTheFirst(), 2);\n");

   [XF] public void a_caller_that_passes_the_member_above_as_a_delegate_says_so() =>
      _answer.Must().Contain("\n    Shapes.Drawing.Sketch.Draw  Shapes.Drawing\\Sketch.cs:17  (passes it as a delegate)\n      21: var areas = _shapes.Select(AreaOf).ToList();\n");
}
