using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   Palette.Mix has two overloads. Overloads share a qualified name, so until 2026-11-24 a name went to whichever
///   overload ReSharper listed first, and said nothing of the other.
/// </summary>
public class When_going_to_the_definition_of_an_overloaded_method(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _byName = sandbox.McpServer.CallTool("go_to_definition", new { symbolName = "Shapes.Palette.Mix" });
   readonly string _atTheThreeColorOverload = sandbox.McpServer.CallTool("go_to_definition", new { filePath = sandbox.PathOf("Shapes/Palette.Mixing.cs"), line = 7, column = 17 });

   [XF] public void by_name_it_goes_to_every_overload() =>
      _byName.Must().Satisfy(it => it.Contains("2 overloads of Shapes.Palette.Mix") && it.Contains("Palette.Mixing.cs:5:17") && it.Contains("Palette.Mixing.cs:7:17"));

   [XF] public void at_a_position_it_goes_to_the_overload_there() =>
      _atTheThreeColorOverload.Must().Satisfy(it => !it.Contains("overloads") && it.Contains("Palette.Mixing.cs:7:17") && !it.Contains("Palette.Mixing.cs:5:"));
}
