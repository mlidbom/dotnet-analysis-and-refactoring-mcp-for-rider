using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>
///   A rename ReSharper reports an error conflict for, here Circle.Area renamed to its own type's name. Rider shows such
///   conflicts before it applies a rename; until 2026-11-24 the tool applied it anyway, and a usage ReSharper had said it
///   couldn't update correctly, a qualified <c>cref</c>, was written broken.
/// </summary>
public class When_a_rename_meets_an_error_conflict(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("rename_symbol", new { symbolName = "Shapes.Circle.Area", newName = "Circle" });
   readonly TheSandboxWithTheFixtureSolution _sandbox = sandbox;

   [XF] public void it_is_not_applied() => _answer.Must().Contain("\"applied\": false").Contain("\"refused\"");
   [XF] public void the_answer_names_the_conflict_and_its_severity() => _answer.Must().Contain("Containing type has the same name as the new name").Contain("\"severity\": \"Error\"");
   [XF] public void it_tells_how_to_apply_it_anyway() => _answer.Must().Contain("applyDespiteConflicts");
   [XF] public void the_file_is_as_it_was() => File.ReadAllText(_sandbox.PathOf("Shapes/Circle.cs")).Must().Contain("public double Area()");
}
