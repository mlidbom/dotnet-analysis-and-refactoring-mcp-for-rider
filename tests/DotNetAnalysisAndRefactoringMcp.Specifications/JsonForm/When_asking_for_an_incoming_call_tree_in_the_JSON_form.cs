using System.Text.Json.Nodes;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.JsonForm;

/// <summary>get_call_hierarchy's incoming tree with format: "json", from Circle.Area, which Drawing.AreaOfTheFirst calls through IShape.Area.</summary>
public class When_asking_for_an_incoming_call_tree_in_the_JSON_form(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly JsonNode _start = JsonNode.Parse(sandbox.McpServer.CallTool("get_call_hierarchy", new { symbolName = "Circle.Area", direction = "incoming", format = "json" }))!["starts"]![0]!;

   [XF] public void it_starts_at_the_member_asked_about() =>
      ((string?)_start["member"]!["qualifiedName"]).Must().Be("Shapes.Circle.Area");

   [XF] public void its_first_caller_is_a_declaration_with_the_line_of_its_call_and_the_member_it_calls_through() =>
      new
      {
         Caller = (string?)_start["callers"]![0]!["member"]!["qualifiedName"],
         Line = (int?)_start["callers"]![0]!["callSites"]![0]!["line"],
         Code = (string?)_start["callers"]![0]!["callSites"]![0]!["code"],
         Through = (string?)_start["callers"]![0]!["callSites"]![0]!["through"]
      }.Must().Be(new
                  {
                     Caller = (string?)"Shapes.Drawing.Drawing.AreaOfTheFirst",
                     Line = (int?)12,
                     Code = (string?)"public double AreaOfTheFirst() => _shapes[0].Area();",
                     Through = (string?)"Shapes.IShape.Area"
                  });
}
