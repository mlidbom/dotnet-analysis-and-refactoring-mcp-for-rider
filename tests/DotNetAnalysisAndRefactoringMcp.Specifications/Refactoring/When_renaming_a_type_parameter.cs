using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>
///   <c>Stacking</c>'s type parameter <c>TShape</c>, renamed for real to <c>TPiece</c>, by the position of its
///   declaration. Its doc comments name it with <c>&lt;typeparamref&gt;</c> tags, as Compze's <c>TeventApplyingPart</c>
///   did, whose type parameters a rename left the tags of stale on 2026-11-18.
/// </summary>
/// <remarks>It changes a file, so it renames once, for every fact.</remarks>
public class When_renaming_a_type_parameter(TheSandboxWithTheFixtureSolution sandbox)
{
   static Observed? _observed;

   readonly Observed _the = _observed ??= Observe(sandbox);

   [XF] public void the_rename_is_applied() => _the.Answer.Must().Contain("\"applied\": true");
   [XF] public void its_uses_in_code_are_renamed() => _the.Text.Must().Satisfy(it => it.Contains("where TPiece : class, IShape") && it.Contains("Put(TPiece shape)"));
   [XF] public void the_typeparamref_tags_to_it_are_renamed() => _the.Text.Must().Satisfy(it => !it.Contains("\"TShape\"") && it.Contains("<typeparamref name=\"TPiece\"/>"));

   sealed record Observed(string Answer, string Text);

   static Observed Observe(TheSandboxWithTheFixtureSolution sandbox)
   {
      const string file = "Shapes.Drawing/Renaming/Stacking.cs";
      sandbox.McpServer.CallTool("rename_symbol", new { filePath = file, line = 5, column = 40, newName = "TPiece", dryRun = true });
      var answer = sandbox.McpServer.CallTool("rename_symbol", new { filePath = file, line = 5, column = 40, newName = "TPiece" });
      return new Observed(answer, File.ReadAllText(sandbox.PathOf(file)));
   }
}
