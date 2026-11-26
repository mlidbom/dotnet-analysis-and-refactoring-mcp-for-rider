using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

/// <summary>
///   Each tool that changes nothing, or in a dry run, called once, in text and in the JSON form, for one item and for a
///   batch, and for a symbol that doesn't exist.
/// </summary>
public class When_every_tool_that_changes_nothing_answers(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly IReadOnlyList<ToolAnswer> _answers = AnswersOfEveryTool(sandbox);

   /// <summary>
   ///   Until 2026-11-26 the tools that wrote their lines with <c>StringBuilder.AppendLine</c>, and every answer of
   ///   indented JSON, ended lines with a carriage return and a line feed; the rest with a line feed alone.
   /// </summary>
   [XF] public void no_answer_holds_a_carriage_return() =>
      _answers.Where(it => it.Text.Contains('\r')).Select(it => it.Call).ToList().Must().Satisfy(it => it.Count == 0);

   static IReadOnlyList<ToolAnswer> AnswersOfEveryTool(TheSandboxWithTheFixtureSolution sandbox)
   {
      var circle = sandbox.PathOf("Shapes/Circle.cs");
      var exporting = sandbox.PathOf("Shapes.Drawing/Exporting.cs");
      (string Tool, object Arguments)[] calls =
      [
         ("search_symbol", new { query = "Circle" }),
         ("search_symbol", new { queries = new[] { "Circle", "Square" } }),
         ("get_symbol_info", new { symbolName = "Circle" }),
         ("get_symbol_info", new { symbolName = "No.Such.Symbol" }),
         ("go_to_definition", new { symbolName = "Circle" }),
         ("go_to_definition", new { symbolName = "Circle", format = "json" }),
         ("get_symbol_source", new { symbolName = "Circle" }),
         ("find_usages", new { symbolName = "IShape.Area" }),
         ("find_usages", new { symbols = new[] { new { symbolName = "IShape.Area" }, new { symbolName = "No.Such.Symbol" } } }),
         ("find_implementations", new { symbolName = "IShape" }),
         ("get_call_hierarchy", new { symbolName = "Circle.Area", direction = "incoming" }),
         ("get_call_hierarchy", new { symbolName = "Circle.Area", direction = "incoming", format = "json" }),
         ("get_type_hierarchy", new { symbolName = "Circle", direction = "supertypes" }),
         ("flow", new { symbolName = "Exporting.ExportedLength" }),
         ("list_symbols_in_file", new { filePath = circle }),
         ("browse_namespace", new { namespaceName = "Shapes", includeMembers = true }),
         ("browse_namespace", new { namespaceName = "Shapes", format = "json" }),
         ("get_solution_structure", new {}),
         ("list_solutions", new {}),
         ("get_file_errors", new { filePath = circle }),
         ("get_diagnostics", new { filePath = exporting, minSeverity = "hint" }),
         ("list_quick_fixes", new { filePath = exporting, line = 9, column = 9 }),
         ("apply_suggestions", new { filePath = exporting, all = true, dryRun = true }),
         ("rename_symbol", new { symbolName = "Circle", newName = "Disk", dryRun = true }),
         ("inspect_code", new { filePath = exporting, minSeverity = "hint" })
      ];
      return [.. calls.Select(it => new ToolAnswer($"{it.Tool} {System.Text.Json.JsonSerializer.Serialize(it.Arguments)}",
                                                   sandbox.McpServer.CallTool(it.Tool, it.Arguments)))];
   }

   record ToolAnswer(string Call, string Text);
}
