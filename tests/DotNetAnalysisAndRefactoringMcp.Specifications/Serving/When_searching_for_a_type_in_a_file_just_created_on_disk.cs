using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

/// <summary>
///   A call that reaches a new file's types by name, not by the file: the file is in a project's folder, which an
///   SDK-style project takes in, but the call doesn't name it.
/// </summary>
public class When_searching_for_a_type_in_a_file_just_created_on_disk(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = CreateAndSearch(sandbox);

   /// <summary>Until 2026-11-17 the disk sync waited only for a new file the call named: the search found nothing.</summary>
   [XF] public void it_finds_the_type() => _answer.Must().Contain("class Triangle");

   static string CreateAndSearch(TheSandboxWithTheFixtureSolution sandbox)
   {
      sandbox.McpServer.ChangeOnDiskAndWaitForTheFileWatcher(() => File.WriteAllText(sandbox.PathOf("Shapes/Triangle.cs"),
                                                                                     """
                                                                                     namespace Shapes;

                                                                                     /// <summary>A triangle whose sides are all <paramref name="side"/> long.</summary>
                                                                                     public sealed class Triangle(double side)
                                                                                     {
                                                                                       public double Side => side;
                                                                                     }

                                                                                     """));
      return sandbox.McpServer.CallTool("search_symbol", new { query = "Triangle" });
   }
}
