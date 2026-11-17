using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Inspections;

/// <summary>
///   The fixture's copy is a git repository whose one commit is the copy, so a file changed in it is changed since
///   HEAD. Other specifications change files too, so what else the answer holds depends on which ran first.
/// </summary>
public class When_inspecting_the_files_changed_since_a_commit(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = ChangeAndInspect(sandbox);
   readonly string _unknownCommit = sandbox.McpServer.CallTool("inspect_code", new { changedSince = "no-such-ref" });

   /// <summary>Until 2026-11-17 inspect_code took only files, folders and projects: changed files were listed by hand.</summary>
   [XF] public void it_says_what_it_inspected() => _answer.Must().StartWith("changed since HEAD: ");

   [XF] public void it_inspects_a_changed_file() => _answer.Must().Contain(@"Shapes\Hexagon.cs");

   /// <summary>Bounds.cs has issues at hint, and no specification changes it.</summary>
   [XF] public void and_not_one_that_did_not_change() => _answer.Must().NotContain("Bounds.cs");

   [XF] public void a_ref_that_does_not_exist_is_an_error_not_the_whole_solution() => _unknownCommit.Must().Contain("Cannot list files changed since no-such-ref");

   static string ChangeAndInspect(TheSandboxWithTheFixtureSolution sandbox)
   {
      File.WriteAllText(sandbox.PathOf("Shapes/Hexagon.cs"),
                        """
                        namespace Shapes;

                        /// <summary>A hexagon whose sides are all <paramref name="side"/> long.</summary>
                        public sealed class Hexagon(double side)
                        {
                          public double Side => side;
                        }

                        """);
      return sandbox.McpServer.CallTool("inspect_code", new { changed = true, minSeverity = "hint" });
   }
}
