using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

public class When_asking_for_a_types_information(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _sealedClass = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "Circle" });
   readonly string _staticClass = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "ShapeExtensions" });
   readonly string _interface = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "IShape" });

   [XF] public void it_gives_its_namespace() => _sealedClass.Must().Contain("namespace: Shapes");

   /// <summary>It gave the line of the type's doc comment, line 3, until 2026-11-14.</summary>
   [XF] public void it_gives_the_line_of_its_name_as_where_it_is_declared() => _sealedClass.Must().Contain("Circle.cs:4:");

   /// <summary>Every type's header said "static" until 2026-11-14: ReSharper's IsStatic is true for every type.</summary>
   [XF] public void it_calls_a_sealed_class_sealed() => _sealedClass.Must().StartWith("sealed class Circle");

   [XF] public void it_calls_a_static_class_static() => _staticClass.Must().StartWith("static class ShapeExtensions");

   [XF] public void it_calls_an_interface_an_interface_and_nothing_more() => _interface.Must().StartWith("interface IShape");

   [XF] public void it_gives_an_interface_no_base_type() => _interface.Must().NotContain("baseTypes");
}

public class When_asking_for_a_types_members(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "Drawing", includeMembers = true });

   /// <summary>Until 2026-11-15 nothing told a private member from a public one.</summary>
   [XF] public void it_says_which_are_private() => _answer.Must().Contain("private field _shapes");

   [XF] public void and_which_are_public() => _answer.Must().Contain("public method AreaOfTheFirst");
}

public class When_asking_for_the_members_of_a_class_with_an_extension_block(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _answer = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "ShapeExtensions", includeMembers = true });
   readonly string _member = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "Diameter" });

   /// <summary>Until 2026-11-17 it was listed as "public sealed class $extension(circle:Circle)".</summary>
   [XF] public void it_lists_the_block_as_CSharp_declares_it() => _answer.Must().Contain("\n  extension(circle:Circle)");

   [XF] public void with_its_property_under_it() => _answer.Must().Contain("\n    public property Diameter : double");

   [XF] public void and_its_method() => _answer.Must().Contain("\n    public method Grown(by:double) : Circle");

   /// <summary>Until 2026-11-17 the property was listed only as the static method the compiler makes of its getter.</summary>
   [XF] public void and_not_the_static_methods_its_members_compile_to() => _answer.Must().NotContain("get_Diameter").NotContain("Grown(circle:Circle");

   /// <summary>Until 2026-11-17 it was "Shapes.ShapeExtensions+$extension".</summary>
   [XF] public void a_member_is_in_the_block_of_its_class() => _member.Must().Contain("containingType: Shapes.ShapeExtensions, extension(circle:Circle)");
}
