using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>
///   A static class whose extension method another file calls only as an extension method. The rider MCP's
///   move_type_to_namespace gives such a file no using, and sometimes leaves the declaration where it was.
/// </summary>
/// <remarks>
///   It changes files, so it moves the type once, for every fact: xUnit makes an instance per fact. Rider saves the
///   changed documents a while after the move, so the files are read once they're saved.
/// </remarks>
public class When_moving_a_type_to_another_namespace(TheSandboxWithTheFixtureSolution sandbox)
{
   static readonly TimeSpan HowLongSavingMayTake = TimeSpan.FromSeconds(30);
   static readonly object Gate = new();
   static Observed? _observed;

   readonly Observed _the = Once(sandbox);

   [XF] public void it_says_it_moved_it() => _the.Answer.Must().Contain("Moved Lengths from Shapes.Drawing.Moving to Shapes.Drawing.Units");

   [XF] public void the_declaration_is_in_the_new_namespace() => _the.MovedFile.Must().Contain("namespace Shapes.Drawing.Units");

   [XF] public void the_file_that_calls_its_extension_method_gets_the_using() => _the.CallingFile.Must().Contain("using Shapes.Drawing.Units;");

   [XF] public void and_has_no_errors() => _the.CallingFileErrors.Must().Contain("0 diagnostics");

   sealed record Observed(string Answer, string MovedFile, string CallingFile, string CallingFileErrors);

   static Observed Once(TheSandboxWithTheFixtureSolution sandbox)
   {
      lock(Gate)
         return _observed ??= Observe(sandbox);
   }

   static Observed Observe(TheSandboxWithTheFixtureSolution sandbox)
   {
      var answer = sandbox.McpServer.CallTool("move_type_to_namespace", new { symbolName = "Shapes.Drawing.Moving.Lengths", newNamespace = "Shapes.Drawing.Units" });
      var calling = sandbox.PathOf("Shapes.Drawing/Moving/Ruler.cs");
      var deadline = DateTime.UtcNow + HowLongSavingMayTake;
      while(!File.ReadAllText(calling).Contains("using Shapes.Drawing.Units;") && DateTime.UtcNow < deadline)
         Thread.Sleep(250);
      return new Observed(answer,
                          File.ReadAllText(sandbox.PathOf("Shapes.Drawing/Moving/Lengths.cs")),
                          File.ReadAllText(calling),
                          sandbox.McpServer.CallTool("get_file_errors", new { filePath = calling }));
   }
}
