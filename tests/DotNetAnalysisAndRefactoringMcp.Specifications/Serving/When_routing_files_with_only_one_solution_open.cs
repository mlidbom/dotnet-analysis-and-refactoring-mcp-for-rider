using Compze.Must;
using Compze.xUnitBDD;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Processes;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public class When_routing_files_with_only_one_solution_open(TheSandboxWithTheFixtureSolution sandbox)
{
   const string Circle = "Shapes/Circle.cs";
   readonly string _openWorkTree = Path.GetDirectoryName(sandbox.PathOf(TheSandboxWithTheFixtureSolution.SolutionFile))!;

   [XF] public void an_absolute_path_in_the_open_work_tree_is_served() =>
      SymbolsIn(sandbox.PathOf(Circle)).Must().Contain("class Circle");

   [XF] public void a_relative_path_in_the_open_work_tree_is_served() =>
      SymbolsIn(Circle).Must().Contain("class Circle");

   [XF] public void a_filename_resolved_by_suffix_in_the_open_solution_is_still_served() =>
      SymbolsIn("Circle.cs").Must().Contain("class Circle");

   [XF] public void a_file_not_yet_created_in_the_open_work_tree_reaches_file_lookup() =>
      SymbolsIn(sandbox.PathOf("Shapes/Future/NotCreated.cs")).Must().Contain("File not found");

   [XF] public void a_sibling_work_trees_file_is_refused_with_both_work_trees_named() =>
      CheckRefusal("..", other => RefusedFile(Path.Combine(other, Circle)));

   [XF] public void a_nested_work_trees_file_is_refused_with_both_work_trees_named() =>
      CheckRefusal(".claude/worktrees", other => RefusedFile(Path.Combine(other, Circle)));

   [XF] public void a_relative_path_into_a_nested_work_tree_is_refused() =>
      CheckRefusal(".claude/worktrees", other => RefusedFile(Path.GetRelativePath(_openWorkTree, Path.Combine(other, Circle))));

   [XF] public void an_absent_file_below_a_nested_work_tree_is_refused() =>
      CheckRefusal(".claude/worktrees", other => RefusedFile(Path.Combine(other, "Future/NotCreated.cs")));

   [XF] public void a_batch_naming_files_in_both_work_trees_is_refused_as_one_call() =>
      CheckRefusal("..", other => sandbox.McpServer.Refusal("get_file_errors",
         new { filePaths = new[] { sandbox.PathOf(Circle), Path.Combine(other, Circle) } }));

   string SymbolsIn(string filePath) => sandbox.McpServer.CallTool("list_symbols_in_file", new { filePath });

   string RefusedFile(string filePath) => sandbox.McpServer.Refusal("list_symbols_in_file", new { filePath });

   void CheckRefusal(string parent, Func<string, string> callForOtherWorkTree)
   {
      var otherWorkTree = sandbox.PathOf($"{parent}/Routing-{Guid.NewGuid():N}");
      ExternalCommand.Run("git", ["worktree", "add", "--quiet", "--detach", otherWorkTree, "HEAD"], _openWorkTree);
      try
      {
         var refusal = callForOtherWorkTree(otherWorkTree);
         refusal.Must().Contain(otherWorkTree);
         refusal.Must().Contain($"{TheSandboxWithTheFixtureSolution.SolutionName} ({_openWorkTree})");
      }
      finally
      {
         ExternalCommand.Run("git", ["worktree", "remove", otherWorkTree], _openWorkTree);
      }
   }
}
