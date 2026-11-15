using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>
///   <c>Bundle</c>, declared in <c>Bundle.cs</c> and used in <c>Packing.cs</c>, renamed for real to <c>Parcel</c>. Until
///   2026-11-15 the file kept the old name, as the tool stopped where Rider's Rename goes on to rename it.
/// </summary>
/// <remarks>
///   It changes files, so it renames once, for every fact: xUnit makes an instance per fact, and runs a class's facts
///   one at a time. The files are read as the answer comes, since an edit tool saves what it changed before it answers.
/// </remarks>
public class When_renaming_a_type(TheSandboxWithTheFixtureSolution sandbox)
{
   static Observed? _observed;

   readonly Observed _the = _observed ??= Observe(sandbox);

   [XF] public void its_file_takes_the_new_name() => _the.FileNames.Must().Satisfy(it => it.Contains("Parcel.cs") && !it.Contains("Bundle.cs"));
   [XF] public void and_holds_the_renamed_type() => _the.Renamed.Must().Contain("public sealed class Parcel;");
   [XF] public void the_file_that_uses_it_is_changed() => _the.Using.Must().Contain("public static Parcel Empty()");
   [XF] public void the_answer_names_the_renamed_file() => _the.Answer.Must().Contain("Parcel.cs");
   [XF] public void and_doesnt_wait_to_save_the_file_of_the_old_name() => _the.Answer.Must().NotContain("hadn't saved");
   [XF] public void and_leaves_no_errors() => _the.Errors.Must().Satisfy(it => it.All(answer => answer.Contains("0 diagnostics")));

   sealed record Observed(string Answer, List<string> FileNames, string Renamed, string Using, List<string> Errors);

   static Observed Observe(TheSandboxWithTheFixtureSolution sandbox)
   {
      var answer = sandbox.McpServer.CallTool("rename_symbol", new { symbolName = "Shapes.Drawing.Renaming.Bundle", newName = "Parcel" });
      string[] files = [PathOf("Parcel.cs"), PathOf("Packing.cs")];
      return new Observed(answer,
                          [.. Directory.GetFiles(sandbox.PathOf("Shapes.Drawing/Renaming")).Select(it => Path.GetFileName(it))],
                          File.ReadAllText(files[0]),
                          File.ReadAllText(files[1]),
                          [.. files.Select(it => sandbox.McpServer.CallTool("get_file_errors", new { filePath = it }))]);

      string PathOf(string file) => sandbox.PathOf($"Shapes.Drawing/Renaming/{file}");
   }
}
