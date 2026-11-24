using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   Palette.Pick has a type parameter, which ReSharper models as a type. In Compze's IThreadGate.cs the members after
///   such a method came under a "(IThreadGate)" heading, as if after a nested type (2026-11-24).
/// </summary>
public class When_outlining_a_file_with_a_generic_method(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _outline = sandbox.McpServer.CallTool("list_symbols_in_file", new { filePath = sandbox.PathOf("Shapes/Palette.cs") });

   [XF] public void the_members_after_it_stay_under_their_type() =>
      _outline.Must().Satisfy(it => it.Contains("Count") && !it.Contains("(Palette)"));
}
