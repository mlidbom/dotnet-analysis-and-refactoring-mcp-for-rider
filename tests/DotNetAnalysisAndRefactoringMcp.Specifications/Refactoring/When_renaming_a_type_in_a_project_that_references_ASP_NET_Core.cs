using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>
///   <c>Exhibit</c>, in <c>Shapes.Web</c>, which references ASP.NET Core, and used in <c>Gallery.cs</c>, renamed for real
///   to <c>Showpiece</c>. Rename asks ReSharper's ASP.NET MVC support for the files to rename with a type, which looks for
///   every controller with a search that runs on several threads at once.
/// </summary>
/// <remarks>
///   Until 2026-11-18 every rename of a type in such a project failed with "OperationCanceledException: The operation was
///   canceled." (in Compze's integration tests), dry runs included: the tool held the write lock throughout, which keeps
///   the search's threads from reading. It changes files, so it renames once, for every fact.
/// </remarks>
public class When_renaming_a_type_in_a_project_that_references_ASP_NET_Core(TheSandboxWithTheFixtureSolution sandbox)
{
   static Observed? _observed;

   readonly Observed _the = _observed ??= Observe(sandbox);

   [XF] public void the_preview_lists_the_files_it_would_change() => _the.Preview.Must().Satisfy(it => it.Contains("Exhibit.cs") && it.Contains("Gallery.cs"));
   [XF] public void the_rename_is_applied() => _the.Answer.Must().StartWith("Renamed sealed class Shapes.Web.Exhibit to Showpiece.");
   [XF] public void its_file_takes_the_new_name() => _the.FileNames.Must().Satisfy(it => it.Contains("Showpiece.cs") && !it.Contains("Exhibit.cs"));
   [XF] public void the_file_that_uses_it_is_changed() => _the.Using.Must().Contain("public static Showpiece Of(IShape shape)");

   sealed record Observed(string Preview, string Answer, List<string> FileNames, string Using);

   static Observed Observe(TheSandboxWithTheFixtureSolution sandbox)
   {
      // A new controller, in the file the call names, which the call syncs first, empties ReSharper's cache of the
      // controllers: the rename's search for them then runs, as it did in Compze.
      File.AppendAllText(sandbox.PathOf("Shapes.Web/Exhibit.cs"), "\n/// <summary>The exhibits, on the web.</summary>\npublic sealed class ExhibitsController : Microsoft.AspNetCore.Mvc.ControllerBase;\n");
      var preview = sandbox.McpServer.CallTool("rename_symbol", new { filePath = "Shapes.Web/Exhibit.cs", line = 4, column = 21, newName = "Showpiece", dryRun = true });
      var answer = sandbox.McpServer.CallTool("rename_symbol", new { symbolName = "Shapes.Web.Exhibit", newName = "Showpiece" });
      return new Observed(preview,
                          answer,
                          [.. Directory.GetFiles(sandbox.PathOf("Shapes.Web")).Select(it => Path.GetFileName(it))],
                          File.ReadAllText(sandbox.PathOf("Shapes.Web/Gallery.cs")));
   }
}
