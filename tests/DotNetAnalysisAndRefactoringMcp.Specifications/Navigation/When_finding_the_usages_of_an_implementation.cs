using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

public class When_finding_the_usages_of_an_implementation(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("find_usages", new { symbolName = "Circle.Area" });

   [XF] public void it_finds_the_usages_through_the_interface() => _answer.Must().Contain("Drawing.cs");

   [XF] public void and_says_which_interface_member_they_use() => _answer.Must().Contain("includes usages through Shapes.IShape.Area");
}

public class When_finding_the_usages_of_an_implementation_of_a_compiled_interface(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("find_usages", new { symbolName = "Canvas.Dispose" });

   /// <summary>Following them would list every <c>using</c> in the solution, whatever it disposes.</summary>
   [XF] public void it_does_not_follow_the_compiled_interface() => _answer.Must().Contain("not usages through the compiled System.IDisposable.Dispose");

   [XF] public void so_it_lists_none_of_the_other_types_disposals() => _answer.Must().NotContain("Exporting.cs");
}
