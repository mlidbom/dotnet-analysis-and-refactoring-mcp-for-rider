using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>
///   The position of <c>stream</c> in <c>using var stream = new MemoryStream();</c> resolved to the Dispose that the
///   <c>using</c> calls, until 2026-11-14, and a rename there renamed the type's Dispose method.
/// </summary>
public class When_renaming_at_the_variable_of_a_using_declaration(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = RenameAt(sandbox, "Shapes.Drawing/Exporting.cs", "using var stream", "stream");

   [XF] public void it_renames_the_variable() => _answer.Must().StartWith("Would rename variable Shapes.Drawing.Exporting.stream to memory:");

   static string RenameAt(TheSandboxWithTheFixtureSolution sandbox, string file, string lineText, string word)
   {
      var path = sandbox.PathOf(file);
      var lines = File.ReadAllLines(path);
      var line = Array.FindIndex(lines, it => it.Contains(lineText));
      var column = lines[line].IndexOf(word, lines[line].IndexOf(lineText, StringComparison.Ordinal), StringComparison.Ordinal);
      return sandbox.McpServer.CallTool("rename_symbol", new { filePath = path, line = line + 1, column = column + 1, newName = "memory", dryRun = true });
   }
}
