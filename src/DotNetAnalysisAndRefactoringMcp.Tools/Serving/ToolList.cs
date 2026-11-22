using System.Collections.Generic;
using System.Linq;
using DotNetAnalysisAndRefactoringMcp.Protocol;
using DotNetAnalysisAndRefactoringMcp.Tools;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>
///   The tools a client gets from <c>tools/list</c>: the open solutions' tools, each with the <c>solutionName</c>
///   parameter that routes a call, after <c>list_solutions</c>, which lists the names to route by.
/// </summary>
static class ToolList
{
   public static List<ToolDefinition> Of(OpenSolutions solutions)
   {
      // This Rider's solutions are served by the same build; a peer's tools are listed only when this Rider has none.
      var served = solutions.Local.Count > 0 ? solutions.Local.Cast<OpenSolution>() : solutions.Peers;
      var tools = served.SelectMany(solution => solution.Tools)
                        .GroupBy(tool => tool.Name)
                        .Select(namesakes => WithSolutionName(namesakes.First()));

      return [ListSolutions, ..tools];
   }

   static ToolDefinition ListSolutions =>
      new()
      {
         Name = "list_solutions",
         Annotations = ToolAnnotations.ReadOnly,
         Description = "List all currently open solutions in Rider. "
                     + "Use this to discover available solution names when multiple solutions are open.",
         InputSchema = new JObject { ["type"] = "object", ["properties"] = new JObject(), ["required"] = new JArray() }
      };

   /// <summary><paramref name="tool"/>, with the parameter <c>solutionName</c> added to its schema.</summary>
   static ToolDefinition WithSolutionName(ToolDefinition tool)
   {
      // A copy: a peer's schema is the registration's, which other requests read at the same time.
      var schema = (JObject)JsonOf.Value(tool.InputSchema).DeepClone();
      var properties = schema["properties"] as JObject ?? new JObject();
      properties["solutionName"] = new JObject
                                   {
                                      ["type"] = "string",
                                      ["description"] = "Target solution name (e.g. 'MyProject'), a unique path segment (e.g. 'my-repo'), or full path. "
                                                      + "Optional when only one solution is open. "
                                                      + "Required when multiple solutions are open — use list_solutions to see available names and uniquePathSegment hints. "
                                                      + "Worktrees or clones of one repository share a name: pick one by its path segment or full path."
                                   };
      schema["properties"] = properties;

      return new ToolDefinition { Name = tool.Name, Description = tool.Description, InputSchema = schema, Annotations = tool.Annotations };
   }
}
