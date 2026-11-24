using System.Text.Json.Nodes;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.JsonForm;

/// <summary>get_call_hierarchy's outgoing tree with format: "json", from the property Sketch.Caption, whose getter makes a Palette and mixes two of its colors.</summary>
public class When_asking_for_an_outgoing_call_tree_in_the_JSON_form(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly JsonNode _start = JsonNode.Parse(sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Shapes.Drawing.Sketch.Caption", direction = "outgoing", format = "json" }))!["starts"]![0]!;

   [XF] public void it_starts_at_the_member_asked_about_with_the_accessors_it_runs() =>
      new { Member = (string?)_start["member"]!["qualifiedName"], Accessors = (string?)_start["accessors"] }.Must().Be(new { Member = (string?)"Shapes.Drawing.Sketch.Caption", Accessors = (string?)"get" });

   [XF] public void each_call_under_it_is_a_declaration_in_the_order_written() =>
      _start["calls"]!.AsArray().Select(it => $"{it!["member"]!["kind"]} {it["member"]!["qualifiedName"]}").ToList()
                      .Must().Satisfy(it => it.SequenceEqual(new[] { "constructor Shapes.Palette.Palette", "method Shapes.Palette.Mix" }));
}
