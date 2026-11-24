using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.OutgoingCallTree;

/// <summary>
///   get_call_hierarchy's outgoing tree, from Sketch.Draw, which calls in every way C# has. Until 2026-11-24 the tree
///   followed only calls through a name, stopped at an interface or a virtual member, listed every call into compiled
///   code, and answered in JSON, repeating each file's full path at every node.
/// </summary>
public class When_asking_for_the_calls_a_method_makes(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _calls = sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Shapes.Drawing.Sketch.Draw", direction = "outgoing" });

   [XF] public void it_starts_with_how_deep_and_into_what_then_the_method_and_where_it_is() =>
      _calls.Must().StartWith("Calls into the solution's code, 2 levels deep:\n\nShapes.Drawing.Sketch.Draw  Shapes.Drawing\\Sketch.cs:17\n");

   [XF] public void a_constructor_it_calls_with_new_is_followed() =>
      _calls.Must().Contain("\n  new Shapes.Drawing.Sketch  Shapes.Drawing\\Sketch.cs:12\n    Shapes.Drawing.Sketch.Ordered  Shapes.Drawing\\Sketch.cs:36\n");

   [XF] public void an_implicit_constructor_is_where_its_type_is() => _calls.Must().Contain("\n  new Shapes.Outline  Shapes\\Outline.cs:4\n");

   [XF] public void an_operator_is_a_call() => _calls.Must().Contain("\n  Shapes.Outline.op_Addition  Shapes\\Outline.cs:12\n");

   [XF] public void a_property_with_code_is_followed_into_its_getter_in_the_order_its_calls_are_written() =>
      _calls.Must().Contain("\n  Shapes.Drawing.Sketch.Caption  Shapes.Drawing\\Sketch.cs:30\n"
                          + "    new Shapes.Palette  Shapes\\Palette.cs:4\n"
                          + "    Shapes.Palette.Mix(first:string, second:string)  Shapes\\Palette.Mixing.cs:5\n");

   [XF] public void an_indexer_is_a_call() => _calls.Must().Contain("\n  Shapes.Drawing.Sketch.this[index:int]  Shapes.Drawing\\Sketch.cs:32\n");

   [XF] public void a_method_passed_as_a_delegate_is_followed_and_says_so() =>
      _calls.Must().Contain("\n  Shapes.Drawing.Sketch.AreaOf  Shapes.Drawing\\Sketch.cs:34  (passed as a delegate)\n");

   [XF] public void a_call_to_an_interfaces_member_lists_its_implementations_under_it() =>
      _calls.Must().Contain("\n    Shapes.IShape.Area  Shapes\\IShape.cs:6\n"
                          + "      implementation: Shapes.Circle.Area  Shapes\\Circle.cs:6\n"
                          + "      implementation: Shapes.Square.Area  Shapes\\Square.cs:6\n");

   /// <summary>
   ///   Its own calls are leaves, as they were under Caption: neither says its calls are listed above, since none of
   ///   theirs are listed anywhere.
   /// </summary>
   [XF] public void a_call_to_a_virtual_member_lists_its_own_calls_then_its_overrides_under_it() =>
      _calls.Must().Contain("\n  Shapes.Drawing.Sketch.Describe  Shapes.Drawing\\Sketch.cs:28\n"
                          + "    new Shapes.Palette  Shapes\\Palette.cs:4\n"
                          + "    Shapes.Palette.Mix(first:string, second:string)  Shapes\\Palette.Mixing.cs:5\n"
                          + "    override: Shapes.Drawing.DetailedSketch.Describe  Shapes.Drawing\\DetailedSketch.cs:6\n");

   [XF] public void a_callee_met_again_points_to_its_calls_listed_above() =>
      _calls.Must().Contain("\n    Shapes.IShape.Area  Shapes\\IShape.cs:6  (its calls are listed above)\n");

   [XF] public void a_local_function_is_named_in_its_method_and_a_call_to_itself_is_recursive() =>
      _calls.Must().Contain("\n  Shapes.Drawing.Sketch.Draw.Nesting  Shapes.Drawing\\Sketch.cs:24\n"
                          + "    Shapes.Drawing.Sketch.Draw.Nesting  Shapes.Drawing\\Sketch.cs:24  (recursive)\n");

   [XF] public void a_nameof_a_cref_and_an_auto_property_call_nothing() =>
      _calls.Must().Satisfy(it => !it.Contains("TryMeasure") && !it.Contains("Pick") && !it.Contains("Title"));

   [XF] public void calls_into_compiled_code_are_not_shown_but_counted_by_assembly() =>
      _calls.Must().Satisfy(it => !it.Contains("Enumerable") && it.Contains("calls into compiled code, in System.Linq (2)"));
}
