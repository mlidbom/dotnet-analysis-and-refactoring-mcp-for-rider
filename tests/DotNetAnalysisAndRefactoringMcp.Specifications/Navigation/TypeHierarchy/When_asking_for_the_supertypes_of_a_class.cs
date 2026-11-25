using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.TypeHierarchy;

/// <summary>StraightSegment derives from Segment, which implements IMeasured.</summary>
public class When_asking_for_the_supertypes_of_a_class(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_type_hierarchy", new { symbolName = "Shapes.Drawing.Renaming.StraightSegment", direction = "supertypes" });

   [XF] public void it_outlines_each_type_under_the_one_that_inherits_from_it() =>
      _answer.Must().Contain("Supertypes, 3 levels up:\n\n"
                           + "sealed class Shapes.Drawing.Renaming.StraightSegment  Shapes.Drawing\\Renaming\\StraightSegment.cs:4\n"
                           + "  abstract class Shapes.Drawing.Renaming.Segment  Shapes.Drawing\\Renaming\\Segment.cs:4\n"
                           + "    interface Shapes.Drawing.Renaming.IMeasured  Shapes.Drawing\\Renaming\\IMeasured.cs:4");

   [XF] public void it_leaves_out_object_which_every_class_inherits_from() =>
      _answer.Must().Satisfy(it => !it.Contains("System.Object"));
}
