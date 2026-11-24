using System.Text.Json.Nodes;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.JsonForm;

/// <summary>
///   go_to_definition with format: "json", which the .NET Terminal Symbol Linker reads instead of the text: one shape
///   for every outcome, {"declarations": [...]}, each as "kind name qualifiedName file:line:column" here.
/// </summary>
public class When_going_to_a_definition_in_the_JSON_form(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly List<string> _type = DeclarationsOf(sandbox, "Circle");
   readonly List<string> _member = DeclarationsOf(sandbox, "Circle.Area");
   readonly List<string> _ambiguous = DeclarationsOf(sandbox, "Area");
   readonly List<string> _unknown = DeclarationsOf(sandbox, "NoSuchSymbol");
   readonly List<string> _recordProperty = DeclarationsOf(sandbox, "Measurement.Unit");
   readonly List<string> _extensionMember = DeclarationsOf(sandbox, "Grown");
   readonly List<string> _typeInParts = DeclarationsOf(sandbox, "Shapes.Palette");
   readonly List<string> _overloaded = DeclarationsOf(sandbox, "Shapes.Palette.Mix");
   readonly JsonNode _theThreeColorOverload = Answer(sandbox, "Shapes.Palette.Mix")["declarations"]![1]!;
   readonly JsonNode _compiled = Answer(sandbox, "System.Math")["declarations"]![0]!;

   [XF] public void a_type_is_one_declaration_where_its_name_is() => _type.Must().Satisfy(it => it.SequenceEqual(["class Circle Shapes.Circle Circle.cs:4:21"]));

   [XF] public void a_member_by_its_type_and_name_too() => _member.Must().Satisfy(it => it.SequenceEqual(["method Area Shapes.Circle.Area Circle.cs:6:17"]));

   [XF] public void an_ambiguous_name_is_every_declaration_of_it() => _ambiguous.Must().Satisfy(it => it.Count > 1 && it.All(declaration => declaration.StartsWith("method Area ")));

   [XF] public void a_name_nothing_has_is_none() => _unknown.Must().Satisfy(it => it.Count == 0);

   /// <summary>Column 41 is the parameter Unit in "public sealed record Measurement(string Unit, double Value)".</summary>
   [XF] public void a_positional_records_property_is_its_parameter() =>
      _recordProperty.Must().Satisfy(it => it.SequenceEqual(["property Unit Shapes.Drawing.Renaming.Measurement.Unit Measurement.cs:4:41"]));

   [XF] public void an_extension_blocks_member_is_in_its_static_class() =>
      _extensionMember.Must().Satisfy(it => it.SequenceEqual(["method Grown Shapes.ShapeExtensions.Grown ShapeExtensions.cs:17:19"]));

   /// <summary>
   ///   Palette is declared in Palette.cs and Palette.Mixing.cs, and ReSharper lists Palette.Mixing.cs first: until
   ///   2026-11-24 the JSON form gave that part, where the other tools gave Palette.cs.
   /// </summary>
   [XF] public void a_type_declared_in_parts_is_in_the_file_named_after_it() =>
      _typeInParts.Must().Satisfy(it => it.SequenceEqual(["class Palette Shapes.Palette Palette.cs:4:29"]));

   /// <summary>Palette.Mix has two overloads: until 2026-11-24 the JSON form gave whichever ReSharper listed first.</summary>
   [XF] public void an_overloaded_members_name_is_every_overload_in_the_order_they_are_declared() =>
      _overloaded.Must().Satisfy(it => it.SequenceEqual(["method Mix Shapes.Palette.Mix Palette.Mixing.cs:5:17", "method Mix Shapes.Palette.Mix Palette.Mixing.cs:7:17"]));

   /// <summary>The symbol linker's picker shows it, to tell the overloads apart.</summary>
   [XF] public void each_with_the_signature_that_tells_it_apart() =>
      ((string?)_theThreeColorOverload["signature"]).Must().Be("method Mix(first:string, second:string, third:string) : string");

   [XF] public void a_compiled_type_is_in_its_assembly_with_no_file() =>
      _compiled.Must().Satisfy(it => it["file"] == null && ((string?)it["assembly"])!.EndsWith(".dll") && (string?)it["kind"] == "class");

   static JsonNode Answer(TheSandboxWithTheFixtureSolution sandbox, string symbolName) =>
      sandbox.McpServer.Json("go_to_definition", new { symbolName, format = "json" });

   static List<string> DeclarationsOf(TheSandboxWithTheFixtureSolution sandbox, string symbolName) =>
      [.. Answer(sandbox, symbolName)["declarations"]!.AsArray().Select(it => $"{it!["kind"]} {it["name"]} {it["qualifiedName"]} {Path.GetFileName((string?)it["file"])}:{it["line"]}:{it["column"]}")];
}
