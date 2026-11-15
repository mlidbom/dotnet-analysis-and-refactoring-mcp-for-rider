using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>
///   <c>IShape.Area</c>, which <c>Circle</c> and <c>Square</c> implement, and <c>Ellipse</c> has a method of the same name
///   of, unrelated. The rename renamed the implementations, and its list of changed files left out their files until
///   2026-11-15.
/// </summary>
public class When_previewing_the_rename_of_an_interface_method(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("rename_symbol", new { symbolName = "Shapes.IShape.Area", newName = "Surface", dryRun = true });

   [XF] public void it_lists_the_file_of_the_interface() => _answer.Must().Contain("IShape.cs");
   [XF] public void and_the_files_of_the_implementations() => _answer.Must().Contain("Circle.cs").Contain("Square.cs");
   [XF] public void and_the_file_that_calls_it() => _answer.Must().Contain("Drawing.cs");
   [XF] public void not_the_file_of_an_unrelated_method_of_the_same_name() => _answer.Must().NotContain("Ellipse.cs");
}
