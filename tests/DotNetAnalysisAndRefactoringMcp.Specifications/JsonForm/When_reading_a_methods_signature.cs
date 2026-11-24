using System.Text.Json.Nodes;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.JsonForm;

/// <summary>
///   The signature go_to_definition's JSON form gives each declaration, which the symbol linker's picker shows, and
///   every tool's text writes the same way. Until 2026-11-24 it wrote no parameter's modifiers or default value, and
///   a compiled method's types as the CLR names them, an array's left empty: Insert(index:Int32, value:) for
///   StringBuilder.Insert(int, char[]).
/// </summary>
public class When_reading_a_methods_signature(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly List<string> _tryMeasure = SignaturesOf(sandbox, "Shapes.Measuring.TryMeasure");
   readonly List<string> _totalArea = SignaturesOf(sandbox, "Shapes.ShapeExtensions.TotalArea");
   readonly List<string> _insert = SignaturesOf(sandbox, "System.Text.StringBuilder.Insert");

   [XF] public void each_parameter_has_its_modifiers_before_its_name_and_its_default_value_after_its_type() =>
      _tryMeasure.Must().Satisfy(it => it.SequenceEqual(["static method TryMeasure(text:string, in scale:double, ref readonly origin:double, ref measured:int, "
                                                       + "out length:double, unit:string = \"cm\", limit:double? = null, params labels:string[]) : bool"]));

   [XF] public void an_extension_methods_receiver_is_marked_this() =>
      _totalArea.Must().Satisfy(it => it.SequenceEqual(["static method TotalArea(this shapes:IEnumerable<IShape>) : double"]));

   [XF] public void a_compiled_methods_types_are_written_as_CSharp_writes_them() =>
      _insert.Must().Contain("method Insert(index:int, value:char[]?) : StringBuilder");

   static List<string> SignaturesOf(TheSandboxWithTheFixtureSolution sandbox, string symbolName) =>
      sandbox.McpServer.Json("go_to_definition", new { symbolName, format = "json" })["declarations"]!.AsArray()
             .Select(it => (string)it!["signature"]!).ToList();
}
