using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Refactoring;

/// <summary>
///   <c>IMeasured.Length</c>, which the abstract <c>Segment</c> implements, <c>StraightSegment</c> overrides,
///   <c>Route</c> calls, and two doc comments name, and of which <c>Rope</c> has an unrelated method of the same name.
///   Renamed for real: until 2026-11-15 only dry runs were checked, and the rule on C# code intelligence sent the renames
///   of types and members to the rider MCP's <c>rename_refactoring</c>.
/// </summary>
/// <remarks>
///   It changes files, so it renames once, for every fact: xUnit makes an instance per fact, and runs a class's facts
///   one at a time. The files are read as the answer comes, since an edit tool saves what it changed before it answers.
/// </remarks>
public class When_renaming_an_interface_method(TheSandboxWithTheFixtureSolution sandbox)
{
   static readonly string[] Changed = ["IMeasured.cs", "Segment.cs", "StraightSegment.cs", "Route.cs"];

   static Observed? _observed;

   readonly Observed _the = _observed ??= Observe(sandbox);

   [XF] public void it_renames_the_interface_method() => _the.Files["IMeasured.cs"].Must().Contain("double Extent();");
   [XF] public void and_its_abstract_implementation() => _the.Files["Segment.cs"].Must().Contain("public abstract double Extent();");
   [XF] public void and_the_override_of_that() => _the.Files["StraightSegment.cs"].Must().Contain("public override double Extent() =>");
   [XF] public void and_the_call() => _the.Files["Route.cs"].Must().Contain("it.Extent()");
   [XF] public void and_the_see_crefs_that_name_it() => _the.Files.Must().Satisfy(it => it["Segment.cs"].Contains("<see cref=\"IMeasured.Extent\"/>") && it["Route.cs"].Contains("<see cref=\"IMeasured.Extent\"/>"));
   [XF] public void not_the_unrelated_method_of_the_same_name() => _the.Files["Rope.cs"].Must().Contain("public double Length() =>");
   [XF] public void it_lists_the_files_it_changed() => _the.Answer.Must().Satisfy(it => Changed.All(it.Contains));
   [XF] public void not_the_file_it_left_alone() => _the.Answer.Must().NotContain("Rope.cs");
   [XF] public void and_leaves_no_errors() => _the.Errors.Must().Satisfy(it => it.All(answer => answer.Contains("0 diagnostics")));

   sealed record Observed(string Answer, Dictionary<string, string> Files, List<string> Errors);

   static Observed Observe(TheSandboxWithTheFixtureSolution sandbox)
   {
      var answer = sandbox.McpServer.CallTool("rename_symbol", new { symbolName = "Shapes.Drawing.Renaming.IMeasured.Length", newName = "Extent" });
      var files = Changed.Append("Rope.cs").ToDictionary(it => it, it => File.ReadAllText(PathOf(it)));
      return new Observed(answer, files, [.. files.Keys.Select(it => sandbox.McpServer.CallTool("get_file_errors", new { filePath = PathOf(it) }))]);

      string PathOf(string file) => sandbox.PathOf($"Shapes.Drawing/Renaming/{file}");
   }
}
