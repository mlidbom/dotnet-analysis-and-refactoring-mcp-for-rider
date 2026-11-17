using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

public class When_a_doc_comment_links_to_other_symbols(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "Grown" });

   /// <summary>Until 2026-11-17 every element without content was left out: "A  whose radius is  longer."</summary>
   [XF] public void it_gives_them_by_their_names() => _answer.Must().Contain("doc: A Circle whose radius is by longer.");
}
