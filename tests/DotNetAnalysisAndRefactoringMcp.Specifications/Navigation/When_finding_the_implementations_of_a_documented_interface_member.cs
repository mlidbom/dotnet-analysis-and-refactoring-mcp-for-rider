using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   ILabeled.Label has a doc comment. Badge implements it under an inheritdoc, Sticker under a summary of its own. Until
///   2026-11-25 find_implementations named an implementing member only by its own name, Label, so only its path told
///   which type it is in.
/// </summary>
public class When_finding_the_implementations_of_a_documented_interface_member(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("find_implementations", new { symbolName = "Shapes.ILabeled.Label" });

   [XF] public void it_names_each_by_its_types_qualified_name_and_gives_only_the_summary_written_on_it() =>
      _answer.Must().Be("abstract method Label() : string — 2 implementations\n"
                      + "\n[direct] method Shapes.Badge.Label — Shapes\\Badge.cs:7"
                      + "\n[direct] method Shapes.Sticker.Label — Shapes\\Sticker.cs:7"
                      + "\n  doc: What is printed on the sticker.");
}
