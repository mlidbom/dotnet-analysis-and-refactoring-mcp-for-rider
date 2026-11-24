using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   Palette.Mix has two overloads, and the three-color one calls the two-color one. Overloads share a qualified name,
///   so until 2026-11-24 a name found the usages of whichever overload ReSharper listed first, and said nothing of the
///   others.
/// </summary>
public class When_finding_the_usages_of_an_overloaded_method(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _byName = sandbox.McpServer.CallTool("find_usages", new { symbolName = "Shapes.Palette.Mix" });
   readonly string _atTheThreeColorOverload = sandbox.McpServer.CallTool("find_usages", new { filePath = sandbox.PathOf("Shapes/Palette.Mixing.cs"), line = 7, column = 17 });

   [XF] public void by_name_it_covers_every_overload() =>
      _byName.Must().Satisfy(it => it.Contains("(all 2 overloads)") && it.Contains("Palette.Mixing.cs:5") && it.Contains("Palette.Mixing.cs:7"));

   [XF] public void by_name_it_finds_the_call_one_overload_makes_to_the_other() =>
      _byName.Must().Contain("Mix(Mix(first, second), third)");

   [XF] public void at_a_position_it_covers_the_overload_there() =>
      _atTheThreeColorOverload.Must().Satisfy(it => !it.Contains("overloads") && it.Contains("0 usages"));
}
