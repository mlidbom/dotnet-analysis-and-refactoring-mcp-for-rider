using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>
///   <c>ILoader</c> and <c>Loaded</c>, the two types of a file with a file-scoped namespace, moved to the same other
///   namespace one after the other. The first move leaves the file with two namespaces, which need blocks; until
///   2026-11-15 the second left the one namespace block-scoped, though the solution's style is file-scoped.
/// </summary>
/// <remarks>
///   It changes files, so it moves them once, for every fact: xUnit makes an instance per fact, and runs a class's facts
///   one at a time. The file is read as the answer comes, since an edit tool saves what it changed before it answers.
/// </remarks>
public class When_moving_the_types_of_a_file_one_after_the_other(TheSandboxWithTheFixtureSolution sandbox)
{
   static Observed? _observed;

   readonly Observed _the = _observed ??= Observe(sandbox);

   [XF] public void the_file_has_one_file_scoped_namespace() => _the.File.Must().Contain("namespace Shapes.Drawing.Loading;").NotContain("namespace Shapes.Drawing.Moving");
   [XF] public void the_second_answer_says_it_made_it_file_scoped_again() => _the.Answers[1].Must().Contain("file-scoped again");
   [XF] public void and_the_file_has_no_errors() => _the.Errors.Must().Contain("0 diagnostics");

   /// <summary>
   ///   One run on 2026-11-15 waited the full 10 s for Rider to save the file after the second move, and noted it unsaved;
   ///   it didn't recur by hand. The note says how the file differed, which is what tells why.
   /// </summary>
   [XF] public void neither_answer_says_rider_hadnt_saved_the_file() => _the.Answers.Must().Satisfy(it => it.All(answer => !answer.Contains("hadn't saved")));

   sealed record Observed(List<string> Answers, string File, string Errors);

   static Observed Observe(TheSandboxWithTheFixtureSolution sandbox)
   {
      var path = sandbox.PathOf("Shapes.Drawing/Moving/Loader.cs");
      List<string> answers = [Move("Shapes.Drawing.Moving.ILoader"), Move("Shapes.Drawing.Moving.Loaded")];
      return new Observed(answers, File.ReadAllText(path), sandbox.McpServer.CallTool("get_file_errors", new { filePath = path }));

      string Move(string type) => sandbox.McpServer.CallTool("move_type_to_namespace", new { symbolName = type, newNamespace = "Shapes.Drawing.Loading" });
   }
}
