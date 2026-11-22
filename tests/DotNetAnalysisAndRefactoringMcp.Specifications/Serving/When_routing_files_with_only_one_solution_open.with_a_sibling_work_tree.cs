using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public partial class When_routing_files_with_only_one_solution_open
{
   public class with_a_sibling_work_tree : When_routing_files_with_only_one_solution_open
   {
      readonly string _siblingWorkTree;
      readonly string _fileRefusal;
      readonly string _batchRefusal;

      public with_a_sibling_work_tree(TheSandboxWithTheFixtureSolution sandbox) : base(sandbox)
      {
         using var sibling = new TemporaryWorkTree(_openWorkTree, "..");
         _siblingWorkTree = sibling.Folder;
         var filePath = Path.Combine(_siblingWorkTree, Circle);
         _fileRefusal = sandbox.McpServer.Refusal("list_symbols_in_file", new { filePath });
         _batchRefusal = sandbox.McpServer.Refusal("get_file_errors", new { filePaths = new[] { sandbox.PathOf(Circle), filePath } });
      }

      [XF] public void a_file_there_is_refused_naming_its_work_tree() => _fileRefusal.Must().Contain(_siblingWorkTree);

      [XF] public void the_refusal_names_the_open_solution_and_its_work_tree() => _fileRefusal.Must().Contain(OpenSolutionWithItsWorkTree);

      [XF] public void a_batch_naming_files_in_both_work_trees_is_refused_naming_the_sibling_work_tree() =>
         _batchRefusal.Must().Contain(_siblingWorkTree);

      [XF] public void the_batch_refusal_names_the_open_solution_and_its_work_tree() => _batchRefusal.Must().Contain(OpenSolutionWithItsWorkTree);
   }
}
