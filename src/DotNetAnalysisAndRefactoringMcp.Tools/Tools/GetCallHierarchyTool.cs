using System.Collections.Generic;
using System.Linq;
using JetBrains.Application.Progress;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Impl;
using JetBrains.ReSharper.Psi.Search;
using JetBrains.ReSharper.Psi.Tree;
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
      "'incoming': who calls it, recursively, in JSON; on an implementation or override, also the calls made through the interface or base member, " +
      "when that member is declared in the solution; a compiled one, such as IDisposable.Dispose, is named in the answer, to ask about by name. " +
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
                                                   direction = new { type = "string", description = "'incoming' (callers of the method) or 'outgoing' (methods called by the method). Required." },
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

      if(direction == "outgoing")
         return Outgoing(arguments, declaredElement, maxDepth);

      // Only method/function-like elements have a call hierarchy.
      if(declaredElement is not IFunction)
         return new
                {
                   error = $"Symbol '{declaredElement.ShortName}' is a " +
                           $"{declaredElement.GetElementType().PresentableName}, not a method/function. " +
                           "Call hierarchy is only available for methods and functions."
                };

      // One node budget for every overload's tree: the call stays bounded however many overloads a name has.
      var budget = new NodeBudget();
      var hierarchies = Overloads.NamedBy(arguments, declaredElement).Cast<IFunction>()
                                 .Select(it => HierarchyOf(it, maxDepth, budget))
                                 .ToList();
      return hierarchies.Count == 1 ? hierarchies[0] : hierarchies;
   }

   /// <summary>The outgoing tree of each member the arguments name: its overloads, for a name.</summary>
   object Outgoing(JObject arguments, IDeclaredElement declaredElement, int maxDepth)
   {
      var members = Overloads.NamedBy(arguments, declaredElement);
      var starts = members.Select(Callee.StartingAt).Where(it => it != null).ToList();
      if(starts.Count == 0)
         return $"{PsiHelpers.GetQualifiedName(declaredElement)} is a {declaredElement.GetElementType().PresentableName}: " +
                "only a method, a constructor, an operator, a local function or a property makes calls.";

      var tree = new OutgoingCallTree(CallScope.From(arguments), maxDepth, new NodeBudget());
      foreach(var start in starts)
         tree.Add(start);

      if(JsonFormat.IsAskedFor(arguments))
         return tree.Json();

      var text = tree.Text(SolutionRoot.Of(_solution));
      return starts.Count > 1 ? $"{Overloads.Heading(members)}. {text}" : text;
   }

   /// <summary>The callers of <paramref name="function"/>, as a tree <paramref name="maxDepth"/> deep.</summary>
   Dictionary<string, object> HierarchyOf(IFunction function, int maxDepth, NodeBudget budget)
   {
      var visited = new HashSet<IDeclaredElement>(DeclaredElementEqualityComparer.ElementComparer);
      var calls = BuildIncoming(function, 0, maxDepth, visited, budget);
      var result = new Dictionary<string, object>
                   {
                      ["symbol"] = function.ShortName,
                      ["qualifiedName"] = PsiHelpers.GetQualifiedName(function),
                      ["signature"] = Signature.Of(function),
                      ["direction"] = "incoming",
                      ["maxDepth"] = maxDepth,
                      ["truncated"] = budget.Truncated,
                      ["calls"] = calls
                   };

      if(SuperMembers.Of(function).Line("calls") is { } note)
         result["note"] = note;

      return result;
   }

   // ---- Incoming (callers) -------------------------------------------------

   // Callers are found through the element's references and through those of the source members it overrides or
   // implements (SuperMembers): a call through an interface is a call to every implementation. A caller is the
   // type member around the reference (method, constructor, property, field), so calls from accessors, local
   // functions and lambdas count as calls from the member they are in.
   List<object> BuildIncoming(
      IDeclaredElement element,
      int depth,
      int maxDepth,
      HashSet<IDeclaredElement> visited,
      NodeBudget budget)
   {
      var children = new List<object>();
      if(depth >= maxDepth || budget.Exhausted)
         return children;

      // Cycle detection: do not expand an element we are already expanding along the current path.
      if(!visited.Add(element))
         return children;

      var psiServices = _solution.GetPsiServices();
      var searchDomain = SearchDomainFactory.Instance.CreateSearchDomain(_solution, false);

      // The same calling member appears once even if it calls the target several times, or both directly and
      // through an interface. Each of its overloads is a caller of its own.
      var seenCallers = new HashSet<IDeclaredElement>(DeclaredElementEqualityComparer.ElementComparer);
      var callerEntries = new List<(IDeclaredElement caller, Dictionary<string, object> node)>();

      foreach(var target in new[] { element }.Concat(SuperMembers.Of(element).InSource))
      {
         if(budget.Exhausted) break;
         var through = target.Equals(element) ? null : PsiHelpers.GetQualifiedName(target);

         psiServices.Finder.FindReferences(
            target,
            searchDomain,
            new FindResultConsumer(findResult =>
            {
               if(budget.Exhausted)
                  return FindExecution.Stop;

               if(!(findResult is FindResultReference reference))
                  return FindExecution.Continue;

               var refNode = reference.Reference.GetTreeNode();

               // Treat the reference as a call only if it is the invoked expression
               // of an invocation; otherwise it's a plain reference (e.g. method group).
               var asExpr = refNode as ICSharpExpression;
               var isCall = asExpr != null && InvocationExpressionNavigator.GetByInvokedExpression(asExpr) != null;

               var caller = refNode.GetContainingNode<ITypeMemberDeclaration>()?.DeclaredElement;
               if(caller == null)
                  return FindExecution.Continue;

               if(!seenCallers.Add(caller))
                  return FindExecution.Continue;

               var node = BuildNode(caller, refNode, isCall, budget, through);
               if(node == null)
                  return FindExecution.Continue;

               callerEntries.Add((caller, node));
               return FindExecution.Continue;
            }),
            NullProgressIndicator.Create());
      }

      foreach(var (caller, node) in callerEntries)
      {
         if(budget.Exhausted) break;
         node["children"] = BuildIncoming(caller, depth + 1, maxDepth, visited, budget);
         children.Add(node);
      }

      // Allow this element to be re-expanded along a different branch of the tree.
      visited.Remove(element);

      return children;
   }

   // ---- Node construction --------------------------------------------------

   /// <summary>
   /// Builds a call-hierarchy node for <paramref name="element"/>, locating it at
   /// the position of <paramref name="siteNode"/> (the call/reference site). Charges
   /// one unit against the node budget. <paramref name="through"/> is the member the call was found through, when
   /// it isn't <paramref name="element"/> itself, such as the interface member it implements.
   /// </summary>
   static Dictionary<string, object> BuildNode(
      IDeclaredElement element,
      ITreeNode siteNode,
      bool isCall,
      NodeBudget budget,
      string through = null)
   {
      if(element == null) return null;
      if(!budget.TryTake()) return null;

      var node = new Dictionary<string, object>
                 {
                    ["method"] = element.ShortName,
                    ["qualifiedName"] = PsiHelpers.GetQualifiedName(element),
                    ["kind"] = isCall ? "call" : "reference"
                 };

      if(siteNode != null)
      {
         var sourceFile = siteNode.GetSourceFile();
         var range = TreeNodeExtensions.GetDocumentRange(siteNode);
         if(sourceFile != null && range.IsValid())
         {
            var (line, column) = PsiHelpers.GetLineColumn(range.StartOffset);
            node["file"] = sourceFile.GetLocation().FullPath;
            node["line"] = line;
            node["column"] = column;
         }
      }

      // Overloads share their qualified name: the signature tells which one.
      if(Overloads.Of(element).Count > 1)
         node["signature"] = Signature.Of(element);

      if(through != null)
         node["through"] = through;

      node["children"] = new List<object>();
      return node;
   }
}
