using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public partial class When_routing_files_with_only_one_solution_open
{
   public class with_a_nested_work_tree : When_routing_files_with_only_one_solution_open
   {
      readonly string _nestedWorkTree;
      readonly string _fileRefusal;
      readonly string _relativePathRefusal;
      readonly string _absentFileRefusal;

      public with_a_nested_work_tree(TheSandboxWithTheFixtureSolution sandbox) : base(sandbox)
      {
         using var nested = new TemporaryWorkTree(_openWorkTree, ".claude/worktrees");
         _nestedWorkTree = nested.Folder;
         var absolute = Path.Combine(_nestedWorkTree, Circle);
         _fileRefusal = RefusalFor(absolute);
         _relativePathRefusal = RefusalFor(Path.GetRelativePath(_openWorkTree, absolute));
         _absentFileRefusal = RefusalFor(Path.Combine(_nestedWorkTree, "Future/NotCreated.cs"));

         string RefusalFor(string filePath) => sandbox.McpServer.Refusal("list_symbols_in_file", new { filePath });
      }

      [XF] public void a_file_there_is_refused_naming_its_work_tree() => _fileRefusal.Must().Contain(_nestedWorkTree);

      [XF] public void the_refusal_names_the_open_solution_and_its_work_tree() => _fileRefusal.Must().Contain(OpenSolutionWithItsWorkTree);

      [XF] public void a_relative_path_into_the_nested_work_tree_is_refused_naming_its_work_tree() => _relativePathRefusal.Must().Contain(_nestedWorkTree);

      [XF] public void the_relative_path_refusal_names_the_open_solution_and_its_work_tree() => _relativePathRefusal.Must().Contain(OpenSolutionWithItsWorkTree);

      [XF] public void a_file_not_yet_created_in_the_nested_work_tree_is_refused_naming_its_work_tree() => _absentFileRefusal.Must().Contain(_nestedWorkTree);

      [XF] public void the_absent_file_refusal_names_the_open_solution_and_its_work_tree() => _absentFileRefusal.Must().Contain(OpenSolutionWithItsWorkTree);
   }
}
