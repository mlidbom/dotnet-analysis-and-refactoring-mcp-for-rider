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

/// <summary>A qualified name was taken for Type.Member until 2026-11-15, and found nothing.</summary>
public class When_searching_for_a_qualified_type_name(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _sourceType = sandbox.McpServer.CallTool("search_symbol", new { query = "Shapes.Circle" });
   readonly string _compiledType = sandbox.McpServer.CallTool("search_symbol", new { query = "System.IO.MemoryStream", includeLibraries = true });
   readonly string _memberOfAQualifiedType = sandbox.McpServer.CallTool("search_symbol", new { query = "Shapes.IShape.Area" });

   [XF] public void it_finds_a_type_of_the_solution() => _sourceType.Must().Contain("class Circle");
   [XF] public void and_a_compiled_one() => _compiledType.Must().Contain("class MemoryStream");
   [XF] public void and_a_member_of_a_qualified_type() => _memberOfAQualifiedType.Must().Contain("method IShape.Area");
}
