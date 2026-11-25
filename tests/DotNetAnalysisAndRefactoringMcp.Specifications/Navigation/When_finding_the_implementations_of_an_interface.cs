using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   Circle and Square implement IShape, each with a doc comment. Until 2026-11-25 find_implementations showed each
///   implementation's first 200 characters of source under it, doc comment first.
/// </summary>
public class When_finding_the_implementations_of_an_interface(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("find_implementations", new { symbolName = "IShape" });

   [XF] public void it_lists_each_with_its_kind_its_qualified_name_where_it_is_and_its_summary() =>
      _answer.Must().Be("interface IShape — 2 implementations\n"
                      + "\n[direct] sealed class Shapes.Circle — Shapes\\Circle.cs:4"
                      + "\n  doc: A circle of a given radius."
                      + "\n[direct] sealed class Shapes.Square — Shapes\\Square.cs:4"
                      + "\n  doc: A square of a given side.");
}
