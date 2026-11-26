using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>
///   <c>IShape.Area</c> previewed renamed, which changes six files, then <c>Palette.Pick</c>, which <c>Palette.cs</c>
///   declares and a <c>cref</c> in <c>Sketch.cs</c> names. Until 2026-11-25 the second preview listed every file the
///   first had rolled back as well: ReSharper's symbol cache bound their declarations anew in the second rename's
///   transaction, which counted them as its own.
/// </summary>
public class When_previewing_a_rename_right_after_previewing_another(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _second = PreviewBoth(sandbox);

   [XF] public void the_second_lists_only_the_files_it_would_change() => _second.Must().EndWith("Would change 2 files:\n  Shapes.Drawing\\Sketch.cs\n  Shapes\\Palette.cs");

   static string PreviewBoth(TheSandboxWithTheFixtureSolution sandbox)
   {
      sandbox.McpServer.CallTool("rename_symbol", new { symbolName = "Shapes.IShape.Area", newName = "Surface", dryRun = true });
      return sandbox.McpServer.CallTool("rename_symbol", new { symbolName = "Shapes.Palette.Pick", newName = "Choose", dryRun = true });
   }
}
