using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public partial class When_routing_files_with_only_one_solution_open
{
   const string Circle = "Shapes/Circle.cs";
   readonly string _openWorkTree;
   readonly string _absolutePathAnswer;
   readonly string _relativePathAnswer;
   readonly string _basenameAnswer;
   readonly string _absentFileAnswer;

   public When_routing_files_with_only_one_solution_open(TheSandboxWithTheFixtureSolution sandbox)
   {
      _openWorkTree = Path.GetDirectoryName(sandbox.PathOf(TheSandboxWithTheFixtureSolution.SolutionFile))!;
      _absolutePathAnswer = SymbolsIn(sandbox.PathOf(Circle));
      _relativePathAnswer = SymbolsIn(Circle);
      _basenameAnswer = SymbolsIn("Circle.cs");
      _absentFileAnswer = SymbolsIn(sandbox.PathOf("Shapes/Future/NotCreated.cs"));

      string SymbolsIn(string filePath) => sandbox.McpServer.CallTool("list_symbols_in_file", new { filePath });
   }

   string OpenSolutionWithItsWorkTree => $"{TheSandboxWithTheFixtureSolution.SolutionName} ({_openWorkTree})";

   [XF] public void an_absolute_path_in_the_open_work_tree_is_served() => _absolutePathAnswer.Must().Contain("class Circle");

   [XF] public void a_relative_path_in_the_open_work_tree_is_served() => _relativePathAnswer.Must().Contain("class Circle");

   [XF] public void a_filename_resolved_by_suffix_in_the_open_solution_is_still_served() => _basenameAnswer.Must().Contain("class Circle");

   [XF] public void a_file_not_yet_created_in_the_open_work_tree_reaches_file_lookup() => _absentFileAnswer.Must().Contain("File not found");
}
