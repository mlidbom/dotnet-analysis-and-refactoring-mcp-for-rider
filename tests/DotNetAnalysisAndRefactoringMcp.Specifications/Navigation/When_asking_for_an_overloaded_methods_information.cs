using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   Palette.Mix has two overloads. Overloads share a qualified name, so until 2026-11-24 a name described whichever
///   overload ReSharper listed first, and said nothing of the other.
/// </summary>
public class When_asking_for_an_overloaded_methods_information(TheSandboxWithTheFixtureSolution sandbox)
{
   const string TheTwoColorOverload = "method Mix(first:string, second:string) : string";
   const string TheThreeColorOverload = "method Mix(first:string, second:string, third:string) : string";

   readonly string _byName = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "Shapes.Palette.Mix" });
   readonly string _atTheThreeColorOverload = sandbox.McpServer.CallTool("get_symbol_info", new { filePath = sandbox.PathOf("Shapes/Palette.Mixing.cs"), line = 7, column = 17 });

   [XF] public void by_name_it_describes_every_overload() =>
      _byName.Must().Satisfy(it => it.Contains("2 overloads of Shapes.Palette.Mix") && it.Contains(TheTwoColorOverload) && it.Contains(TheThreeColorOverload));

   [XF] public void at_a_position_it_describes_the_overload_there() =>
      _atTheThreeColorOverload.Must().Satisfy(it => !it.Contains("overloads") && it.Contains(TheThreeColorOverload) && !it.Contains(TheTwoColorOverload));
}
