using DotNetAnalysisAndRefactoringMcp.TypeHierarchies;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

public class GetTypeHierarchyTool : IMcpTool
{
   readonly ISolution _solution;

   public GetTypeHierarchyTool(ISolution solution) => _solution = solution;

   public string Name => "get_type_hierarchy";
   public ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

   public string Description =>
      "The inheritance hierarchy of a type, as an indented outline of 'kind Namespace.Type  path:line'. " +
      "Direction 'supertypes' goes up to its base classes and the interfaces it implements, compiled ones included; " +
      "'subtypes' goes down to the solution's derived classes and implementations. " +
      "Provide either a symbolName (e.g. 'MyClass', 'Namespace.IMyInterface') or a file path with position. " +
      "maxDepth bounds the levels (default 3).";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   symbolName = new { type = "string", description = "Type name (e.g. 'MyClass', 'Namespace.IMyInterface'). Alternative to filePath+line+column." },
                                                   kind = new { type = "string", description = "Filter by symbol kind when using symbolName: 'type', 'method', 'property', 'field', 'event'. Helps disambiguate when multiple symbols share a name." },
                                                   filePath = new { type = "string", description = "Absolute path to the file containing the type" },
                                                   line = new { type = "integer", description = "1-based line number of the type" },
                                                   column = new { type = "integer", description = "1-based column number of the type" },
                                                   direction = new { type = "string", description = "Required. 'supertypes' (up to base types) or 'subtypes' (down to derived types)." },
                                                   maxDepth = new { type = "integer", description = "Levels to list (default 3)." }
                                                },
                                   required = new[] { "direction" }
                                };

   public object Execute(JObject arguments)
   {
      var direction = arguments["direction"]?.ToString();
      if(string.IsNullOrEmpty(direction))
         return new { error = "Provide 'direction': either 'supertypes' or 'subtypes'." };

      direction = direction.ToLowerInvariant();
      if(direction != "supertypes" && direction != "subtypes")
         return new { error = $"Invalid direction '{direction}'. Use 'supertypes' or 'subtypes'." };

      var maxDepth = arguments["maxDepth"]?.Value<int>() ?? 3;
      if(maxDepth < 1) maxDepth = 1;

      var (declaredElement, error) = PsiHelpers.ResolveFromArgs(
         _solution,
         arguments["symbolName"]?.ToString(),
         arguments["kind"]?.ToString(),
         arguments["filePath"]?.ToString(),
         arguments["line"]?.Value<int>() ?? 0,
         arguments["column"]?.Value<int>() ?? 0);

      if(error != null) return error;
      using var context = CompilationContexts.For(declaredElement);

      if(declaredElement is not ITypeElement type)
         return new { error = $"Symbol '{declaredElement.ShortName}' is not a type. Type hierarchy requires a class, interface, struct, or other type." };

      TypeHierarchy hierarchy = direction == "subtypes"
                                   ? Subtypes.Of(type, maxDepth, _solution)
                                   : Supertypes.Of(type, maxDepth);
      return hierarchy.Text(SolutionRoot.Of(_solution));
   }
}
