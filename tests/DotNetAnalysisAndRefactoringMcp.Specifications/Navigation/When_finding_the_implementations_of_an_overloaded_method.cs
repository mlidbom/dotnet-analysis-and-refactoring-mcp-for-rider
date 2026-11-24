using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   IBlender.Blend has two overloads, each implemented by EvenBlender. Overloads share a qualified name, so until
///   2026-11-24 a name found the implementations of whichever overload ReSharper listed first, and said nothing of the
///   other.
/// </summary>
public class When_finding_the_implementations_of_an_overloaded_method(TheSandboxWithTheFixtureSolution sandbox)
{
   const string TheTwoColorImplementation = "Blend(string first, string second) =>";
   const string TheShareImplementation = "Blend(string first, string second, double share) =>";

   readonly string _byName = sandbox.McpServer.CallTool("find_implementations", new { symbolName = "Shapes.IBlender.Blend" });
   readonly string _atTheShareOverload = sandbox.McpServer.CallTool("find_implementations", new { filePath = sandbox.PathOf("Shapes/IBlender.cs"), line = 8, column = 10 });

   [XF] public void by_name_it_finds_the_implementations_of_every_overload() =>
      _byName.Must().Satisfy(it => it.Contains("2 overloads of Shapes.IBlender.Blend") && it.Contains(TheTwoColorImplementation) && it.Contains(TheShareImplementation));

   [XF] public void at_a_position_it_finds_those_of_the_overload_there() =>
      _atTheShareOverload.Must().Satisfy(it => !it.Contains("overloads") && it.Contains(TheShareImplementation) && !it.Contains(TheTwoColorImplementation));
}
