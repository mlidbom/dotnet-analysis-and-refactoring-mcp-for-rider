using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>
///   A static class whose extension method another file calls only as an extension method. The rider MCP's
///   move_type_to_namespace gives such a file no using, and sometimes leaves the declaration where it was.
/// </summary>
/// <remarks>
///   It changes files, so it moves the type once, for every fact: xUnit makes an instance per fact, and runs a class's
///   facts one at a time. The files are read as the answer comes: an edit tool saves what it changed before it answers,
///   and until 2026-11-15 Rider saved them a while later.
/// </remarks>
public class When_moving_a_type_to_another_namespace(TheSandboxWithTheFixtureSolution sandbox)
{
   static Observed? _observed;

   readonly Observed _the = _observed ??= Observe(sandbox);

   [XF] public void it_says_it_moved_it() => _the.Answer.Must().Contain("Moved Lengths from Shapes.Drawing.Moving to Shapes.Drawing.Units");

   [XF] public void the_declaration_is_in_the_new_namespace() => _the.MovedFile.Must().Contain("namespace Shapes.Drawing.Units");

   [XF] public void the_file_that_calls_its_extension_method_gets_the_using() => _the.CallingFile.Must().Contain("using Shapes.Drawing.Units;");

   [XF] public void and_has_no_errors() => _the.CallingFileErrors.Must().Contain("0 diagnostics");

   [XF] public void the_file_moves_to_the_folder_of_the_namespace() => _the.FileNames.Must().Satisfy(it => it.Contains("Units/Lengths.cs") && !it.Contains("Moving/Lengths.cs"));

   [XF] public void and_the_answer_says_so() => _the.Answer.Must().Contain("Moved the file to").Contain("Lengths.cs");

   sealed record Observed(string Answer, string MovedFile, string CallingFile, string CallingFileErrors, List<string> FileNames);

   static Observed Observe(TheSandboxWithTheFixtureSolution sandbox)
   {
      var answer = sandbox.McpServer.CallTool("move_type_to_namespace", new { symbolName = "Shapes.Drawing.Moving.Lengths", newNamespace = "Shapes.Drawing.Units" });
      var calling = sandbox.PathOf("Shapes.Drawing/Moving/Ruler.cs");
      var drawing = sandbox.PathOf("Shapes.Drawing");
      return new Observed(answer,
                          File.ReadAllText(sandbox.PathOf("Shapes.Drawing/Units/Lengths.cs")),
                          File.ReadAllText(calling),
                          sandbox.McpServer.CallTool("get_file_errors", new { filePath = calling }),
                          [.. Directory.GetFiles(drawing, "*.cs", SearchOption.AllDirectories).Select(it => Path.GetRelativePath(drawing, it).Replace(Path.DirectorySeparatorChar, '/'))]);
   }
}
