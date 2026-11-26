using System.Text.Json.Nodes;
using Compze.Must;
using Compze.xUnitBDD;
using static DotNetAnalysisAndRefactoringMcp.Specifications.TheFixtureSourceGenerator;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.JsonForm;

/// <summary>
///   get_call_hierarchy's incoming tree with format: "json", from Shapes.Ruler.Doubled, which
///   <see cref="TheFixtureSourceGenerator"/> writes in Ruler.g.cs, and which the generated Swatch.Shade calls, in
///   Swatch.g.cs.
/// </summary>
public class When_asking_who_calls_generated_code_in_the_JSON_form(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly JsonNode _start = JsonNode.Parse(sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Shapes.Ruler.Doubled", direction = "incoming", format = "json" }))!["starts"]![0]!;

   JsonNode CallOfShade => _start["callers"]!.AsArray().Single(it => (string?)it!["member"]!["qualifiedName"] == "Shapes.Swatch.Shade")!["callSites"]![0]!;

   /// <summary>Until 2026-11-26 its file was "", and nothing said which generated file the line is in.</summary>
   [XF] public void a_call_in_generated_code_is_on_its_line_in_its_generated_file() =>
      new { File = (string?)CallOfShade["file"], Line = (int?)CallOfShade["line"], Generator = (string?)CallOfShade["generator"], HintName = (string?)CallOfShade["hintName"] }
        .Must().Be(new { File = (string?)null, Line = (int?)6, Generator = (string?)GeneratorName, HintName = (string?)"Swatch.g.cs" });

   [XF] public void the_generated_member_it_starts_at_is_in_its_generated_file() =>
      new { HintName = (string?)_start["member"]!["hintName"], Line = (int?)_start["member"]!["line"] }.Must().Be(new { HintName = (string?)"Ruler.g.cs", Line = (int?)6 });
}
