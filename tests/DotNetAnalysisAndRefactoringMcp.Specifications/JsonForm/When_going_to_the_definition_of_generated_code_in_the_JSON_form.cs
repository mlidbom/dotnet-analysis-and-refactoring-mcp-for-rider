using System.Text.Json.Nodes;
using Compze.Must;
using Compze.xUnitBDD;
using static DotNetAnalysisAndRefactoringMcp.Specifications.TheFixtureSourceGenerator;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.JsonForm;

/// <summary>
///   go_to_definition with format: "json" of what <see cref="TheFixtureSourceGenerator"/> writes, which no file with a
///   path declares. The form was agreed with the session symbol-linker, whose linker reads it, on 2026-11-26.
/// </summary>
public class When_going_to_the_definition_of_generated_code_in_the_JSON_form(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly JsonNode _whollyGeneratedType = DeclarationOf(sandbox, "Shapes.Ruler");
   readonly JsonNode _memberGeneratedIntoAPartialType = DeclarationOf(sandbox, "Shapes.Swatch.Shade");

   /// <summary>Until 2026-11-26 it had no file, no line and no assembly: nothing said where it is.</summary>
   [XF] public void a_wholly_generated_type_is_at_its_name_in_its_generated_file_which_has_no_path() =>
      WhereIs(_whollyGeneratedType).Must().Be(new Where(File: null, Line: 4, Column: 21, Generator: GeneratorName, HintName: "Ruler.g.cs", Assembly: null));

   /// <summary>Until 2026-11-26 it was at its partial type's name in Shapes/Swatch.cs, the file of the type's other part.</summary>
   [XF] public void a_member_generated_into_a_partial_type_is_at_its_name_in_its_own_generated_file() =>
      WhereIs(_memberGeneratedIntoAPartialType).Must().Be(new Where(File: null, Line: 6, Column: 15, Generator: GeneratorName, HintName: "Swatch.g.cs", Assembly: null));

   static JsonNode DeclarationOf(TheSandboxWithTheFixtureSolution sandbox, string symbolName) =>
      sandbox.McpServer.Json("go_to_definition", new { symbolName, format = "json" })["declarations"]![0]!;

   static Where WhereIs(JsonNode declaration) =>
      new((string?)declaration["file"], (int?)declaration["line"], (int?)declaration["column"],
          (string?)declaration["generator"], (string?)declaration["hintName"], (string?)declaration["assembly"]);

   record Where(string? File, int? Line, int? Column, string? Generator, string? HintName, string? Assembly);
}
