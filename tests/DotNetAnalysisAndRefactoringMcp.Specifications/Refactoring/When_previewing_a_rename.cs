using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

public class When_previewing_a_rename(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("rename_symbol", new { symbolName = "Circle", newName = "Disk", dryRun = true });

   [XF] public void it_lists_the_file_that_declares_it() => _answer.Must().Contain("Circle.cs");
   [XF] public void and_the_file_that_uses_it() => _answer.Must().Contain("Drawing.cs");
   [XF] public void it_changes_nothing_on_disk() => File.ReadAllText(sandbox.PathOf("Shapes/Circle.cs")).Must().Contain("class Circle");
}
