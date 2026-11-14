using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

public class When_searching_for_part_of_a_type_name(TheSandboxWithTheFixtureSolution sandbox)
{
  readonly string _answer = sandbox.McpServer.CallTool("search_symbol", new { query = "Circ" });

  [XF] public void it_lists_the_type() => _answer.Must().Contain("class Circle");
  [XF] public void with_the_file_it_is_declared_in() => _answer.Must().Contain("Circle.cs");
}
