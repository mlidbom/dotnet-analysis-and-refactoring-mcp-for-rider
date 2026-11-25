using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.TypeHierarchy;

public class When_asking_for_the_subtypes_of_a_class_that_has_none(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_type_hierarchy", new { symbolName = "Shapes.Drawing.Renaming.StraightSegment", direction = "subtypes" });

   [XF] public void it_says_so_above_the_type() =>
      _answer.Must().Contain("No subtypes in the solution:\n\nsealed class Shapes.Drawing.Renaming.StraightSegment  Shapes.Drawing\\Renaming\\StraightSegment.cs:4");
}
