using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Navigation;

/// <summary>
///   A C# 14 extension block's method, which ReSharper's model has twice: in a class named $extension, and as the static
///   method it compiles to.
/// </summary>
public class When_naming_a_member_of_an_extension_block(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly string _byItsName = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "Grown" });
   readonly string _inItsClass = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "ShapeExtensions.Grown" });
   readonly string _propertyInItsClass = sandbox.McpServer.CallTool("get_symbol_info", new { symbolName = "ShapeExtensions.Diameter" });
   readonly string _searched = sandbox.McpServer.CallTool("search_symbol", new { query = "Grown" });

   /// <summary>Until 2026-11-17 the name was ambiguous, between the block's method and the static method it compiles to.</summary>
   [XF] public void its_name_finds_it() => _byItsName.Must().StartWith("method Grown(by:double) : Circle");

   [XF] public void and_so_does_its_name_in_its_class() => _inItsClass.Must().StartWith("method Grown(by:double) : Circle");

   /// <summary>Until 2026-11-27 it was not found: the static class has the property as its accessor, get_Diameter.</summary>
   [XF] public void a_propertys_name_in_its_class_finds_it_too() => _propertyInItsClass.Must().StartWith("property Diameter : double");

   /// <summary>Until 2026-11-17 a search named it "$extension.Grown".</summary>
   [XF] public void a_search_names_it_in_its_class() => _searched.Must().Contain("method ShapeExtensions.Grown");
}
