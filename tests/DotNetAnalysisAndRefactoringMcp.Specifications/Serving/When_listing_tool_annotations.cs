using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public class When_listing_tool_annotations
{
   readonly IReadOnlyList<JsonElement> _tools;

   public When_listing_tool_annotations(TheSandboxWithTheFixtureSolution sandbox) => _tools = sandbox.McpServer.ListTools();

   string[] ToolsWith(string hint, bool value) =>
      _tools.Where(it => it.TryGetProperty("annotations", out var annotations)
                      && annotations.TryGetProperty(hint, out var property) && property.GetBoolean() == value)
            .Select(it => it.GetProperty("name").GetString()!).Order(StringComparer.Ordinal).ToArray();

   [XF] public void queries_and_completion_are_marked_read_only() =>
      ToolsWith("readOnlyHint", true).Must().SequenceEqual([
         "browse_namespace", "complete_at", "find_implementations", "find_usages", "flow", "get_call_hierarchy",
         "get_diagnostics", "get_file_errors", "get_solution_structure", "get_symbol_info", "get_symbol_source",
         "get_type_hierarchy", "go_to_definition", "inspect_code", "list_quick_fixes", "list_solutions",
         "list_symbols_in_file", "search_symbol"]);

   [XF] public void source_edits_and_project_reload_are_marked_as_state_changing() =>
      ToolsWith("readOnlyHint", false).Must().SequenceEqual(["apply_quick_fix", "apply_suggestions", "fix_usings", "format_file",
                                                            "generate_members", "move_type_to_namespace", "reload_projects", "rename_symbol"]);

   [XF] public void fixing_usings_and_generating_members_make_only_additive_changes() =>
      ToolsWith("destructiveHint", false).Must().SequenceEqual(["fix_usings", "generate_members"]);

   [XF] public void quick_fixes_scoped_suggestions_and_project_reload_may_interact_outside_the_solution() =>
      ToolsWith("openWorldHint", true).Must().SequenceEqual(["apply_quick_fix", "apply_suggestions", "reload_projects"]);

   [XF] public void no_state_changing_tool_promises_that_repeating_a_call_has_no_further_effect() =>
      ToolsWith("idempotentHint", false).Must().SequenceEqual(ToolsWith("readOnlyHint", false));
}
