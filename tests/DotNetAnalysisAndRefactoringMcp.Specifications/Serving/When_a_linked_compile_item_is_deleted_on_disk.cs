using Compze.Must;
using Compze.xUnitBDD;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public class When_a_linked_compile_item_is_deleted_on_disk(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly SandboxMcpServer _server = sandbox.McpServer;
   readonly string _linked = sandbox.PathOf("Shared/StencilPattern.cs");
   readonly string _otherFile = sandbox.PathOf("Shapes/DrawingStencil.cs");

   [XF] public void only_the_first_read_warns_that_the_deleted_file_is_still_in_the_project_model() =>
      WhileTheLinkedFileIsDeleted(() =>
      {
         var first = ReadOtherFile();
         var later = ReadOtherFile();
         later.Must().Contain("DrawingStencil");
         later.Must().Satisfy(it => !it.Contains("StencilPattern.cs"));
         first.Must().Contain("StencilPattern.cs");
         first.Must().Contain("deleted on disk");
         first.Must().Contain("still in Rider's project model");
      });

   [XF] public void only_the_first_edit_is_refused_while_the_deleted_file_remains_in_the_project_model() =>
      WhileTheLinkedFileIsDeleted(() =>
      {
         var first = EditOtherFile();
         var later = EditOtherFile();
         later.Must().Contain("no unresolved type references found");
         File.Exists(_linked).Must().BeFalse();
         first.Must().Contain("Refused:");
         first.Must().Contain("StencilPattern.cs");
         first.Must().Contain("deleted on disk");
         first.Must().Contain("still in Rider's project model");
      });

   [XF] public void overlapping_first_calls_warn_the_reader_and_refuse_the_edit() =>
      WhileTheLinkedFileIsDeleted(() =>
      {
         var read = Task.Run(ReadOtherFile);
         var edit = EditOtherFile();
         read.GetAwaiter().GetResult().Must().Contain("deleted on disk");
         edit.Must().Contain("Refused:");
         edit.Must().Contain("deleted on disk");
      });

   string ReadOtherFile() => _server.CallTool("list_symbols_in_file", new { filePath = _otherFile });
   string EditOtherFile() => _server.CallTool("fix_usings", new { filePath = _otherFile });

   void WhileTheLinkedFileIsDeleted(Action check)
   {
      _server.CallTool("list_symbols_in_file", new { filePath = _linked }).Must().Contain("class StencilPattern");
      var original = File.ReadAllBytes(_linked);
      try
      {
         _server.ChangeOnDiskAndWaitForTheFileWatcher(() => File.Delete(_linked));
         check();
      }
      finally
      {
         _server.ChangeOnDiskAndWaitForTheFileWatcher(() => File.WriteAllBytes(_linked, original));
      }
   }
}
