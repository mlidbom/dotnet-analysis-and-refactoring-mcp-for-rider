using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>Badge.Label's doc comment is an inheritdoc, of ILabeled.Label's summary.</summary>
public class When_going_to_the_definition_of_a_method_that_inherits_its_doc_comment(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("go_to_definition", new { symbolName = "Shapes.Badge.Label" });

   [XF] public void it_gives_the_summary_it_inherits() => _answer.Must().Contain("\ndoc: The text to show for it.\n");
}
