using Compze.Must;
using Compze.xUnitBDD;
using static DotNetAnalysisAndRefactoringMcp.Specifications.TheFixtureSourceGenerator;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.GeneratedCode;

/// <summary>
///   search_symbol for "u", which the names of Shapes.Ruler and Shapes.Ruler.Doubled hold: both of them
///   <see cref="TheFixtureSourceGenerator"/> writes, in Ruler.g.cs.
/// </summary>
public class When_searching_for_generated_code(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _found = sandbox.McpServer.CallTool("search_symbol", new { query = "u", maxResults = 200 });

   /// <summary>
   ///   Until 2026-11-26 every generated declaration was the same to the search, so it listed only the first it found.
   /// </summary>
   [XF] public void it_lists_each_generated_declaration_it_finds_in_its_generated_file() =>
      _found.Must().Satisfy(it => it.Contains($"{FileNamed("Ruler.g.cs")}:4") && it.Contains($"{FileNamed("Ruler.g.cs")}:6"));
}
