using System.Text.Json.Nodes;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.JsonForm;

/// <summary>
///   go_to_definition with format: "json" of what <see cref="TheFixtureSourceGenerator"/> writes, which no file with a
///   path declares. The .NET Terminal Symbol Linker reads this form, so what it says of generated code changes only once
///   the session symbol-linker has a specification for it.
/// </summary>
public class When_going_to_the_definition_of_generated_code_in_the_JSON_form(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly JsonNode _whollyGeneratedType = DeclarationOf(sandbox, "Shapes.Ruler");
   readonly JsonNode _memberGeneratedIntoAPartialType = DeclarationOf(sandbox, "Shapes.Swatch.Shade");

   [XF] public void a_wholly_generated_type_has_no_file_no_line_and_no_assembly() =>
      new { File = (string?)_whollyGeneratedType["file"], Line = (int?)_whollyGeneratedType["line"], Assembly = (string?)_whollyGeneratedType["assembly"] }
        .Must().Be(new { File = (string?)null, Line = (int?)null, Assembly = (string?)null });

   [XF(Skip = "Known bug: the JSON form puts it at its partial type's name in Shapes/Swatch.cs, through DeclarationSite.WhereWrittenInAFileWithAPath, "
            + "until the session symbol-linker has a specification for the change (2026-11-26).")]
   public void a_member_generated_into_a_partial_type_is_not_in_the_types_other_part() =>
      ((string?)_memberGeneratedIntoAPartialType["file"]).Must().Satisfy(it => it == null || !it.EndsWith(@"\Shapes\Swatch.cs"));

   static JsonNode DeclarationOf(TheSandboxWithTheFixtureSolution sandbox, string symbolName) =>
      sandbox.McpServer.Json("go_to_definition", new { symbolName, format = "json" })["declarations"]![0]!;
}
