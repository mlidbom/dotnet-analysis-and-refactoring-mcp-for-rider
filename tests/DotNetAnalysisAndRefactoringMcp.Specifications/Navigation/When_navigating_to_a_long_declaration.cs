using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

public class When_navigating_to_a_long_declaration(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _default = sandbox.McpServer.CallTool("go_to_definition", new { symbolName = "Shapes.Drawing.Drawing" });
   readonly string _explicit = sandbox.McpServer.CallTool("go_to_definition", new { symbolName = "Shapes.Drawing.Drawing", maxTextLength = 200 });
   readonly string _full = sandbox.McpServer.CallTool("go_to_definition", new { symbolName = "Shapes.Drawing.Drawing", maxTextLength = 2000 });

   [XF] public void omitting_the_length_uses_the_advertised_limit() => _default.Must().Be(_explicit);
   [XF] public void the_default_contains_the_first_two_hundred_source_characters_and_an_ellipsis() =>
      Source(_default).Must().Be(Source(_full)[..200] + "...");

   static string Source(string answer) => answer[(answer.IndexOf('\n') + 1)..];
}
