using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.IncomingCallTree;

/// <summary>
///   Sketch's constructor: Sketch.Draw calls it with <c>new</c>, DetailedSketch's primary constructor through its base
///   type, and SketchBook in a field's initializer.
/// </summary>
public class When_asking_who_calls_a_constructor(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_call_hierarchy", new { filePath = sandbox.PathOf("Shapes.Drawing/Sketch.cs"), line = 12, column = 10, direction = "incoming" });

   [XF] public void it_starts_at_the_constructor() =>
      _answer.Must().Contain("\nnew Shapes.Drawing.Sketch  Shapes.Drawing\\Sketch.cs:12\n");

   [XF] public void it_lists_a_method_that_calls_it_with_new() =>
      _answer.Must().Contain("\n  Shapes.Drawing.Sketch.Draw  Shapes.Drawing\\Sketch.cs:17\n    19: var copy = new Sketch(_shapes);\n");

   [XF] public void it_lists_a_derived_types_primary_constructor_that_calls_it_as_its_base() =>
      _answer.Must().Contain("\n  new Shapes.Drawing.DetailedSketch  Shapes.Drawing\\DetailedSketch.cs:4\n    4: public sealed class DetailedSketch() : Sketch(new Circle(1))\n");

   [XF] public void it_lists_a_field_whose_initializer_calls_it_saying_that_its_callers_are_not_followed() =>
      _answer.Must().Contain("\n  Shapes.Drawing.SketchBook._first  Shapes.Drawing\\SketchBook.cs:6  (in no method's or property's code: its callers aren't followed)\n    6: readonly Sketch _first = new Sketch(new Circle(1));");

   [XF] public void it_lists_the_callers_of_its_callers() =>
      _answer.Must().Contain("\n    Shapes.Drawing.SketchBook.Cover  Shapes.Drawing\\SketchBook.cs:8\n      8: public string Cover() => _first.Draw();\n");
}
