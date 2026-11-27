using Compze.Must;
using Compze.xUnitBDD;
using Xunit;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithASecondSolutionOfTheWorkTree;

/// <summary>Until 2026-11-27 the call went to the solution listed first, this Rider's, which doesn't have the file.</summary>
[Collection(nameof(WithTheOutlinesSolutionInThePeer))]
public class When_a_call_names_a_file_of_a_project_only_the_peers_solution_lists(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("list_symbols_in_file", new { filePath = sandbox.PathOf(TheOutlinesSolutionInThePeer.OutlinesFile) });

   [XF] public void the_peers_solution_answers_it() => _answer.Must().Contain("sealed class Outline");
}
