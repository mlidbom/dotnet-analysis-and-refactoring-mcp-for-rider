using System.Text.Json.Nodes;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.JsonForm;

/// <summary>
///   browse_namespace with format: "json" and includeMembers, which the .NET Terminal Symbol Linker builds its index
///   of names from: the types of a namespace, their members, and the types nested in them.
/// </summary>
public class When_browsing_a_namespace_in_the_JSON_form(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly JsonArray _namespaces = sandbox.McpServer.Json("browse_namespace", new { namespaceNames = new[] { "Shapes", "No.Such.Namespace" }, includeMembers = true, format = "json" })["namespaces"]!.AsArray();

   JsonNode Shapes => _namespaces[0]!;
   JsonNode NotFound => _namespaces[1]!;

   JsonNode Type(string name) => Shapes["types"]!.AsArray().Single(it => (string?)it!["name"] == name)!;

   static List<string> MembersOf(JsonNode type) => [.. type["members"]!.AsArray().Select(it => $"{it!["access"]} {it["kind"]} {it["name"]}".Trim())];

   [XF] public void it_lists_the_namespaces_in_it() => Shapes["childNamespaces"]!.AsArray().Select(it => (string?)it).Must().Satisfy(it => it.Contains("Shapes.Drawing"));

   [XF] public void a_type_with_where_its_name_is() => $"{Type("Circle")["kind"]} {Type("Circle")["qualifiedName"]} {Type("Circle")["line"]}:{Type("Circle")["column"]}".Must().Be("class Shapes.Circle 4:21");

   /// <summary>Palette is declared in Palette.cs and Palette.Mixing.cs, which ReSharper lists first.</summary>
   [XF] public void a_type_declared_in_parts_where_its_name_is_in_the_file_named_after_it() =>
      $"{Path.GetFileName((string?)Type("Palette")["file"])}:{Type("Palette")["line"]}:{Type("Palette")["column"]}".Must().Be("Palette.cs:4:29");

   [XF] public void with_its_members_by_their_C_sharp_names() => MembersOf(Type("Circle")).Must().Satisfy(it => it.SequenceEqual(["public method Area", "public constructor Circle"]));

   [XF] public void a_member_of_each_kind() =>
      MembersOf(Type("Outline")).Must().Satisfy(it => it.SequenceEqual(["public field MostPoints", "public event Changed", "public indexer this", "public operator op_Addition", "method Dispose"]));

   [XF] public void an_explicit_interface_implementation_names_its_interface() =>
      Type("Outline")["members"]!.AsArray().Single(it => (string?)it!["name"] == "Dispose")!["explicitInterface"]!.ToString().Must().Be("IDisposable");

   /// <summary>A delegate's members are the compiler's: none of them is listed.</summary>
   [XF] public void the_nested_types_each_with_its_kind_and_members() =>
      Type("Outline")["nestedTypes"]!.AsArray().Select(it => $"{it!["kind"]} {it["qualifiedName"]}: {string.Join(", ", it["members"]!.AsArray().Select(member => member!["name"]))}")
                                     .Must().Satisfy(it => it.SequenceEqual(["class Shapes.Outline.Corner: Angle",
                                                                             "enum Shapes.Outline.Direction: Clockwise, Anticlockwise",
                                                                             "record struct Shapes.Outline.Point: Point, X, Y",
                                                                             "delegate Shapes.Outline.Redrawn: "]));

   [XF] public void an_extension_block_with_its_receiver_and_members() =>
      Type("ShapeExtensions")["members"]!.AsArray().Single(it => (string?)it!["kind"] == "extension")!
                                         .Must().Satisfy(it => it["name"] == null
                                                            && it["receiver"]!.ToJsonString() == """{"name":"circle","type":"Circle","typeName":"Circle"}"""
                                                            && string.Join(", ", it["members"]!.AsArray().Select(member => member!["name"])) == "Diameter, Grown");

   [XF] public void a_namespace_the_solution_does_not_have_is_an_error_of_its_own() => NotFound["error"]!.ToString().Must().Be("Namespace not found: No.Such.Namespace");

   /// <summary>
   ///   Shapes.Ruler, which <see cref="TheFixtureSourceGenerator"/> writes in Ruler.g.cs. Until 2026-11-26 it had no
   ///   file and no line.
   /// </summary>
   [XF] public void a_type_a_source_generator_wrote_whole_with_its_members_and_its_generated_file() =>
      new
      {
         File = (string?)Type("Ruler")["file"],
         Where = $"{Type("Ruler")["line"]}:{Type("Ruler")["column"]}",
         Generator = (string?)Type("Ruler")["generator"],
         HintName = (string?)Type("Ruler")["hintName"],
         Members = string.Join(", ", MembersOf(Type("Ruler")))
      }.Must().Be(new { File = (string?)null, Where = "4:21", Generator = (string?)TheFixtureSourceGenerator.GeneratorName, HintName = (string?)"Ruler.g.cs", Members = "public method Doubled" });

   /// <summary>Shapes.Swatch, whose Shade <see cref="TheFixtureSourceGenerator"/> writes in Swatch.g.cs.</summary>
   [XF] public void a_partial_type_with_the_members_a_source_generator_wrote_into_it_among_its_own() =>
      Type("Swatch")["members"]!.AsArray().Select(it => (string)it!["name"]!).Order().Must().Satisfy(it => it.SequenceEqual(["Color", "Describe", "Shade"]));
}
