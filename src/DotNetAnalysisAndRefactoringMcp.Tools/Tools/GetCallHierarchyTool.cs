using System.Linq;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using DotNetAnalysisAndRefactoringMcp.CallTrees;
using DotNetAnalysisAndRefactoringMcp.Tools.Json;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

public class GetCallHierarchyTool : IMcpTool
{
   /// <summary>The deepest a tree goes, whatever the call asks for.</summary>
   const int MaxDepthCap = 4;

   readonly ISolution _solution;

   public GetCallHierarchyTool(ISolution solution) => _solution = solution;

   public string Name => "get_call_hierarchy";
   public ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

   public string Description =>
      "Build a call hierarchy tree for a method, constructor or property. " +
      "'outgoing': what it calls, recursively, as an indented outline of 'Type.Member  path:line', paths relative to the work tree: " +
      "calls through a name, new, properties with code, indexers and operators, and methods passed as delegates. " +
      "A call to an interface, abstract or virtual member lists its implementations or overrides under it. " +
      "By default only the solution's own code: 'namespaces' narrows that, 'assemblies' adds compiled assemblies; the answer counts the calls it left out. " +
      "'incoming': the code that calls it, recursively, in the same outline, each caller with the lines on which it calls the member above, as find_usages shows them; " +
      "on an implementation or override, also the calls made through the interface or base member, when that member is declared in the solution; " +
      "a compiled one, such as IDisposable.Dispose, is named in the answer, to ask about by name. 'namespaces' narrows the callers too. " +
      "Provide either a symbolName (e.g. 'MyClass.MyMethod') or a file path with position (line/column), plus a 'direction'. " +
      "A method's name gives a tree for each of its overloads; a position, for the one there. " +
      $"Recursion is bounded by 'maxDepth' (default 2, capped at {MaxDepthCap}) and a budget of {NodeBudget.Nodes} nodes.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   symbolName = new { type = "string", description = "Method/function name to build the hierarchy from (e.g. 'MyClass.MyMethod', 'Namespace.MyClass.MyMethod'). Alternative to filePath+line+column." },
                                                   kind = new { type = "string", description = "Filter by symbol kind when using symbolName: 'type', 'method', 'property', 'field', 'event'. Helps disambiguate when multiple symbols share a name." },
                                                   filePath = new { type = "string", description = "Absolute path to the file containing the method" },
                                                   line = new { type = "integer", description = "1-based line number of the method" },
                                                   column = new { type = "integer", description = "1-based column number of the method" },
                                                   direction = new { type = "string", description = "'incoming' (the code that calls it) or 'outgoing' (what its code calls). Required." },
                                                   maxDepth = new { type = "integer", description = "How many levels deep to recurse (default 2, capped at 4)." },
                                                   namespaces = CallScope.NamespacesArgument,
                                                   assemblies = CallScope.AssembliesArgument,
                                                   format = JsonFormat.Argument
                                                },
                                   required = new[] { "direction" }
                                };

   public object Execute(JObject arguments)
   {
      var direction = arguments["direction"]?.ToString()?.Trim().ToLowerInvariant();
      if(direction != "incoming" && direction != "outgoing")
         return new { error = "Provide 'direction' as either 'incoming' or 'outgoing'." };

      var maxDepth = arguments["maxDepth"]?.Value<int>() ?? 2;
      if(maxDepth < 1) maxDepth = 1;
      if(maxDepth > MaxDepthCap) maxDepth = MaxDepthCap;

      var (declaredElement, error) = PsiHelpers.ResolveFromArgs(
         _solution,
         arguments["symbolName"]?.ToString(),
         arguments["kind"]?.ToString(),
         arguments["filePath"]?.ToString(),
         arguments["line"]?.Value<int>() ?? 0,
         arguments["column"]?.Value<int>() ?? 0);

      if(error != null) return error;
      using var context = CompilationContexts.For(declaredElement);

      var members = Overloads.NamedBy(arguments, declaredElement);
      var starts = members.Select(Callee.StartingAt).OfType<Callee>().ToList();
      if(starts.Count == 0)
         return $"{PsiHelpers.GetQualifiedName(declaredElement)} is a {declaredElement.GetElementType().PresentableName}: " +
                "only a method, a constructor, an operator, a local function or a property has a call tree.";

      var scope = CallScope.From(arguments);
      // One tree, with one node budget, for every overload: the call stays bounded however many overloads a name has.
      ICallTree tree = direction == "outgoing"
                          ? new OutgoingCallTree(scope, maxDepth, new NodeBudget())
                          : new IncomingCallTree(_solution, scope, maxDepth, new NodeBudget());
      foreach(var start in starts)
         tree.Add(start);

      if(JsonFormat.IsAskedFor(arguments))
         return tree.Json();

      var text = tree.Text(SolutionRoot.Of(_solution));
      return starts.Count > 1 ? $"{Overloads.Heading(members)}. {text}" : text;
   }
}
