using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

/// <summary>
///   The disk sync's reloading a file just edited on disk sets ReSharper indexing it for tens of milliseconds, and until
///   2026-11-15 most answers right after an edit said the index was still building, of an indexed solution. Five edits,
///   each with a call right after it.
/// </summary>
public class When_calling_a_tool_on_a_file_just_edited_on_disk(TheSandboxWithTheFixtureSolution sandbox)
{
   const int Edits = 5;

   readonly List<string> _answers = EditAndCall(sandbox);

   [XF] public void no_answer_says_the_index_is_still_building() => _answers.Must().Satisfy(it => it.All(answer => !answer.Contains("index still building")));

   static List<string> EditAndCall(TheSandboxWithTheFixtureSolution sandbox)
   {
      var path = sandbox.PathOf("Shapes/Square.cs");
      return [..Enumerable.Range(1, Edits).Select(EditAndCallOnce)];

      string EditAndCallOnce(int edit)
      {
         File.AppendAllText(path, $"// edit {edit}{Environment.NewLine}");
         return sandbox.McpServer.CallTool("get_file_errors", new { filePath = path });
      }
   }
}
