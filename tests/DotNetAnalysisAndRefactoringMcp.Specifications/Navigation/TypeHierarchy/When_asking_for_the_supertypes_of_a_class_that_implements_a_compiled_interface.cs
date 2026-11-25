using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation.TypeHierarchy;

/// <summary>Outline implements IDisposable.</summary>
public class When_asking_for_the_supertypes_of_a_class_that_implements_a_compiled_interface(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_type_hierarchy", new { symbolName = "Shapes.Outline", direction = "supertypes" });

   [XF] public void it_lists_the_interface_with_the_assembly_it_is_in() =>
      _answer.Must().Contain("\n  interface System.IDisposable  [assembly System.Runtime.dll]");
}
