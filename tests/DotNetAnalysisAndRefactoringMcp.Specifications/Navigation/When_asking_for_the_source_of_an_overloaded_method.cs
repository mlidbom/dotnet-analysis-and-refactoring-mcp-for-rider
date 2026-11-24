using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   Palette.Mix has two overloads. A member's name finds one of them, since they share it: asked for JAStudio's
///   NoteSerializer.Serialize with allDeclarations, get_symbol_source returned one of its four overloads (2026-11-24).
/// </summary>
public class When_asking_for_the_source_of_an_overloaded_method(TheSandboxWithTheFixtureSolution sandbox)
{
   const string TheTwoParameterOverload = "Mix(string first, string second) =>";
   const string TheThreeParameterOverload = "Mix(string first, string second, string third) =>";

   readonly string _allDeclarations = sandbox.McpServer.CallTool("get_symbol_source", new { symbolName = "Shapes.Palette.Mix", allDeclarations = true });
   readonly string _source = sandbox.McpServer.CallTool("get_symbol_source", new { symbolName = "Shapes.Palette.Mix" });

   [XF] public void all_its_declarations_are_every_overload() =>
      _allDeclarations.Must().Satisfy(it => it.Contains(TheTwoParameterOverload) && it.Contains(TheThreeParameterOverload));

   [XF] public void its_source_is_one_overload_and_says_how_many_there_are() =>
      _source.Must().Satisfy(it => it.Contains(TheTwoParameterOverload) != it.Contains(TheThreeParameterOverload) && it.Contains("one of 2"));
}
