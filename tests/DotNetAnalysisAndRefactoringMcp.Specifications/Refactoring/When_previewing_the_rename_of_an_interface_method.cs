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

   /// <summary>Until 2026-11-25 the answer was JSON, each file's full path in it and then its name again.</summary>
   [XF] public void it_says_what_it_would_rename_and_lists_the_files_by_their_paths_in_the_work_tree() =>
      _answer.Must().Be("""
                        Would rename abstract method Shapes.IShape.Area to Surface: a dry run, which changed nothing and renamed no file.
                        Would change 6 files:
                          Shapes.Drawing\Drawing.cs
                          Shapes.Drawing\Sketch.cs
                          Shapes\Circle.cs
                          Shapes\IShape.cs
                          Shapes\ShapeExtensions.cs
                          Shapes\Square.cs
                        """.ReplaceLineEndings("\n"));
}
