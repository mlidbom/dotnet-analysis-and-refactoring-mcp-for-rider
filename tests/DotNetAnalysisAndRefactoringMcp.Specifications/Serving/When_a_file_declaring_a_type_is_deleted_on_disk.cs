using Compze.Must;
using Compze.xUnitBDD;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

/// <summary>Each tool gets the first call after deleting the stencil's file from the sandbox copy.
/// The file that uses the stencil belongs to a project referencing the stencil's project.</summary>
public class When_a_file_declaring_a_type_is_deleted_on_disk(TheSandboxWithTheFixtureSolution sandbox)
{
   const string TypeName = "DrawingStencil";
   readonly SandboxMcpServer _server = sandbox.McpServer;
   readonly string _declaration = sandbox.PathOf("Shapes/DrawingStencil.cs");
   readonly string _consumer = sandbox.PathOf("Shapes.Drawing/StencilLibrary.cs");

   [XF] public void search_symbol_by_name_finds_no_declaration() =>
      ImmediatelyAfterDeletingTheFile(() => _server.CallTool("search_symbol", new { query = TypeName, kinds = "type" }))
        .Must().Contain(" — 0 results");

   [XF] public void find_usages_by_name_reports_the_symbol_was_not_found() =>
      ImmediatelyAfterDeletingTheFile(FindUsages).Must().Contain($"Symbol not found: {TypeName}");

   [XF] public void get_file_errors_reports_the_unresolved_type_in_the_referencing_project() =>
      ImmediatelyAfterDeletingTheFile(GetConsumerErrors).Must().Contain($"Cannot resolve symbol '{TypeName}'");

   string ImmediatelyAfterDeletingTheFile(Func<string> firstCall)
   {
      GetConsumerErrors().Must().Contain(" — 0 diagnostics");
      FindUsages().Must().Contain("StencilLibrary.cs");
      var original = File.ReadAllBytes(_declaration);
      try
      {
         File.Delete(_declaration);
         return firstCall();
      }
      finally
      {
         File.WriteAllBytes(_declaration, original);
      }
   }

   string GetConsumerErrors() => _server.CallTool("get_file_errors", new { filePath = _consumer });
   string FindUsages() => _server.CallTool("find_usages", new { symbolName = TypeName, kind = "type" });
}
