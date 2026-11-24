using System.Collections.Generic;
using System.Linq;
using JetBrains.Application.Progress;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Impl;
using JetBrains.ReSharper.Psi.Search;
using JetBrains.ReSharper.Psi.Tree;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

public class GetCallHierarchyTool : IMcpTool
{
   // Cap on the total number of nodes we add to the tree, to keep the
   // operation bounded well inside the 30s R# read-lock budget.
   const int NodeBudget = 300;

   // Hard cap on the requested depth.
   const int MaxDepthCap = 4;

   readonly ISolution _solution;

   public GetCallHierarchyTool(ISolution solution) => _solution = solution;

   public string Name => "get_call_hierarchy";
   public ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

   public string Description =>
      "Build a call hierarchy tree for a method/function. " +
      "'incoming' finds callers (who calls this method, recursively); " +
      "'outgoing' finds callees (which methods this method calls, recursively). " +
      "Provide either a symbolName (e.g. 'MyClass.MyMethod') or a file path with position (line/column), " +
      "plus a 'direction'. A method's name gives a tree for each of its overloads; a position, for the one there. " +
      "Recursion is bounded by 'maxDepth' (default 2, capped at 4) and an internal node budget. " +
      "Outgoing resolution is C#-specific; non-C# roots degrade gracefully. " +
      "On an implementation or override, it also finds the calls made through the interface or base member, when that " +
      "member is declared in the solution; a compiled one, such as IDisposable.Dispose, is named in the answer, to ask about by name.";

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
                                                   maxDepth = new { type = "integer", description = "How many levels deep to recurse (default 2, capped at 4)." }
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

      // Only method/function-like elements have a call hierarchy.
      if(declaredElement is not IFunction)
         return new
                {
                   error = $"Symbol '{declaredElement.ShortName}' is a " +
                           $"{declaredElement.GetElementType().PresentableName}, not a method/function. " +
                           "Call hierarchy is only available for methods and functions."
                };

      // One node budget for every overload's tree: the call stays bounded however many overloads a name has.
      var budget = new NodeBudgetCounter();
      var hierarchies = Overloads.NamedBy(arguments, declaredElement).Cast<IFunction>()
                                 .Select(it => HierarchyOf(it, direction, maxDepth, budget))
                                 .ToList();
      return hierarchies.Count == 1 ? hierarchies[0] : hierarchies;
   }

   /// <summary>The callers or callees of <paramref name="function"/>, as a tree <paramref name="maxDepth"/> deep.</summary>
   Dictionary<string, object> HierarchyOf(IFunction function, string direction, int maxDepth, NodeBudgetCounter budget)
   {
      var visited = new HashSet<IDeclaredElement>(DeclaredElementEqualityComparer.ElementComparer);
      object note = null;

      List<object> calls;
      if(direction == "incoming")
      {
         calls = BuildIncoming(function, 0, maxDepth, visited, budget);
         note = SuperMembers.Of(function).Line("calls");
      } else
      {
         var decls = function.GetDeclarations();
         ICSharpFunctionDeclaration csDecl = null;
         foreach(var decl in decls)
         {
            if(decl is ICSharpFunctionDeclaration f)
            {
               csDecl = f;
               break;
            }
         }

         if(csDecl == null)
         {
            // Non-C# root (or no source declaration available): outgoing analysis
            // is C#-specific, so degrade gracefully with an empty list and a note.
            calls = new List<object>();
            note = "Outgoing call resolution is only supported for C# methods with an available source declaration.";
         } else
         {
            calls = BuildOutgoing(csDecl, 0, maxDepth, visited, budget);
         }
      }

      var result = new Dictionary<string, object>
                   {
                      ["symbol"] = function.ShortName,
                      ["qualifiedName"] = PsiHelpers.GetQualifiedName(function),
                      ["signature"] = PsiHelpers.FormatSignature(function),
                      ["direction"] = direction,
                      ["maxDepth"] = maxDepth,
                      ["truncated"] = budget.Truncated,
                      ["calls"] = calls
                   };

      if(note != null)
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
      NodeBudgetCounter budget)
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

   // ---- Outgoing (callees) -------------------------------------------------

   List<object> BuildOutgoing(
      ICSharpFunctionDeclaration declaration,
      int depth,
      int maxDepth,
      HashSet<IDeclaredElement> visited,
      NodeBudgetCounter budget)
   {
      var children = new List<object>();
      if(depth >= maxDepth || budget.Exhausted)
         return children;

      var declElement = declaration.DeclaredElement;
      if(declElement != null && !visited.Add(declElement))
         return children;

      // The same callee appears once per parent; each of its overloads is a callee of its own.
      var seenCallees = new HashSet<IDeclaredElement>(DeclaredElementEqualityComparer.ElementComparer);
      var calleeEntries = new List<(IFunction calleeFn, Dictionary<string, object> node)>();

      // The whole declaration, not its Body: an expression-bodied member has none, and until 2026-11-24 its calls
      // were missed.
      foreach(var invocation in declaration.Descendants<IInvocationExpression>())
      {
         if(budget.Exhausted) break;

         var reference = invocation.InvokedExpression as IReferenceExpression;
         if(reference == null) continue;

         var callee = reference.Reference.Resolve().DeclaredElement as IFunction;
         if(callee == null) continue;

         if(!seenCallees.Add(callee))
            continue;

         var node = BuildNode(callee, invocation, true, budget);
         if(node == null) continue;

         calleeEntries.Add((callee, node));
      }

      foreach(var (calleeFn, node) in calleeEntries)
      {
         if(budget.Exhausted) break;

         // Recurse into the callee's C# declaration, if we can find one.
         ICSharpFunctionDeclaration calleeDecl = null;
         foreach(var d in calleeFn.GetDeclarations())
         {
            if(d is ICSharpFunctionDeclaration f)
            {
               calleeDecl = f;
               break;
            }
         }

         node["children"] = calleeDecl != null
                               ? BuildOutgoing(calleeDecl, depth + 1, maxDepth, visited, budget)
                               : new List<object>();

         children.Add(node);
      }

      if(declElement != null) visited.Remove(declElement);

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
      NodeBudgetCounter budget,
      string through = null)
   {
      if(element == null) return null;
      if(!budget.TryConsume()) return null;

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
         node["signature"] = PsiHelpers.FormatSignature(element);

      if(through != null)
         node["through"] = through;

      node["children"] = new List<object>();
      return node;
   }

   /// <summary>
   /// Tracks the global node budget across the whole recursion to bound work.
   /// </summary>
   class NodeBudgetCounter
   {
      int _remaining = NodeBudget;
      public bool Truncated { get; private set; }

      public bool Exhausted => _remaining <= 0;

      public bool TryConsume()
      {
         if(_remaining <= 0)
         {
            Truncated = true;
            return false;
         }

         _remaining--;
         return true;
      }
   }
}
