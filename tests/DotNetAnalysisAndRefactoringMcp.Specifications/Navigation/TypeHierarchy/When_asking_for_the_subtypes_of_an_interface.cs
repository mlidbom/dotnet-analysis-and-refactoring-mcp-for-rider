using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.TypeHierarchy;

/// <summary>Segment implements IMeasured, and StraightSegment derives from Segment.</summary>
public class When_asking_for_the_subtypes_of_an_interface(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_type_hierarchy", new { symbolName = "Shapes.Drawing.Renaming.IMeasured", direction = "subtypes" });

   [XF] public void it_outlines_each_type_under_the_one_it_inherits_from() =>
      _answer.Must().Contain("Subtypes in the solution, 3 levels down:\n\n"
                           + "interface Shapes.Drawing.Renaming.IMeasured  Shapes.Drawing\\Renaming\\IMeasured.cs:4\n"
                           + "  abstract class Shapes.Drawing.Renaming.Segment  Shapes.Drawing\\Renaming\\Segment.cs:4\n"
                           + "    sealed class Shapes.Drawing.Renaming.StraightSegment  Shapes.Drawing\\Renaming\\StraightSegment.cs:4");

   [XF] public void it_lists_the_type_that_inherits_through_another_only_under_that_one() =>
      _answer.Must().Satisfy(it => !it.Contains("\n  sealed class Shapes.Drawing.Renaming.StraightSegment"));
}
