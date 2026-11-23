using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   Palette is declared in Palette.cs and Palette.Mixing.cs, which ReSharper lists first. Asked for Compze's
///   IThreadGate, get_symbol_source returned its private implementation's part, and the agent read IThreadGate.cs after
///   it (2026-11-24).
/// </summary>
public class When_asking_for_a_type_declared_in_parts(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _source = sandbox.McpServer.CallTool("get_symbol_source", new { symbolName = "Shapes.Palette" });
   readonly string _definition = sandbox.McpServer.CallTool("go_to_definition", new { symbolName = "Shapes.Palette" });
   readonly string _allDeclarations = sandbox.McpServer.CallTool("get_symbol_source", new { symbolName = "Shapes.Palette", allDeclarations = true });

   [XF] public void its_source_is_the_part_in_the_file_named_after_it() =>
      _source.Must().Satisfy(it => it.Contains("Palette.cs") && !it.Contains("Palette.Mixing.cs"));

   [XF] public void its_definition_is_the_part_in_the_file_named_after_it() =>
      _definition.Must().Satisfy(it => it.Contains(@"Shapes\Palette.cs:") && !it.Contains("Palette.Mixing.cs"));

   [XF] public void all_its_declarations_start_with_the_part_in_the_file_named_after_it() =>
      _allDeclarations.Must().Satisfy(it => it.IndexOf("Palette.cs", StringComparison.Ordinal) < it.IndexOf("Palette.Mixing.cs", StringComparison.Ordinal)
                                         && it.Contains("Palette.Mixing.cs"));
}
