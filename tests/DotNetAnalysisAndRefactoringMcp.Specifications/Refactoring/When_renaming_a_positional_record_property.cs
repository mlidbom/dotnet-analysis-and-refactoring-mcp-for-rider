using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>
///   <c>Measurement.Value</c>, a property that the positional record <c>Measurement</c>'s parameter declares, read in
///   the record and in <c>Survey</c>. Named as the property, it was refused until 2026-11-15: "has no source file".
/// </summary>
/// <remarks>
///   It changes files, so it renames once, for every fact: xUnit makes an instance per fact, and runs a class's facts
///   one at a time. The files are read as the answer comes, since an edit tool saves what it changed before it answers.
/// </remarks>
public class When_renaming_a_positional_record_property(TheSandboxWithTheFixtureSolution sandbox)
{
   static Observed? _observed;

   readonly Observed _the = _observed ??= Observe(sandbox);

   [XF] public void it_renames_the_parameter_that_declares_it() => _the.Record.Must().Contain("record Measurement(string Unit, double Amount)");
   [XF] public void and_its_use_in_the_record() => _the.Record.Must().Contain("$\"{Amount} {Unit}\"");
   [XF] public void and_its_use_in_another_file() => _the.Survey.Must().Contain("it => it.Amount");
   [XF] public void and_leaves_no_errors() => _the.Errors.Must().Satisfy(it => it.All(answer => answer.Contains("0 diagnostics")));

   sealed record Observed(string Answer, string Record, string Survey, List<string> Errors);

   static Observed Observe(TheSandboxWithTheFixtureSolution sandbox)
   {
      var answer = sandbox.McpServer.CallTool("rename_symbol", new { symbolName = "Shapes.Drawing.Renaming.Measurement.Value", kind = "property", newName = "Amount" });
      string[] files = [PathOf("Measurement.cs"), PathOf("Survey.cs")];
      return new Observed(answer,
                          File.ReadAllText(files[0]),
                          File.ReadAllText(files[1]),
                          [.. files.Select(it => sandbox.McpServer.CallTool("get_file_errors", new { filePath = it }))]);

      string PathOf(string file) => sandbox.PathOf($"Shapes.Drawing/Renaming/{file}");
   }
}
