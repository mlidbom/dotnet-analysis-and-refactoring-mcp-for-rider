using System;
using System.Linq;
using System.Text;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

public class GetSymbolSourceTool : IMcpAlwaysLoadedTool
{
   /// <summary>Maximum length of a single declaration's source body before truncation.</summary>
   const int MaxSourceLength = 20000;

   readonly ISolution _solution;

   public GetSymbolSourceTool(ISolution solution) => _solution = solution;

   public string Name => "get_symbol_source";
   public ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

   public string Description =>
      "Get the FULL declaration source code of a symbol (class, method, property, etc.), " +
      "not just a short snippet. Provide either a symbolName (e.g. 'MyClass' or " +
      "'Namespace.MyClass') or a file path with position (line/column). " +
      "Answers the declaration's whole lines, numbered as a file read shows them, under 'Name — path:first-last'. " +
      "By default returns only the primary declaration; set allDeclarations=true to return " +
      "every partial declaration / overload. A declaration over 20,000 characters is cut at a line, saying where to read on. " +
      "Symbols compiled into referenced DLLs return their type's source decompiled by ReSharper.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   symbolName = new { type = "string", description = "Symbol name to get source for (e.g. 'MyClass', 'Namespace.MyClass', 'MyClass.MyMethod'). Alternative to filePath+line+column." },
                                                   kind = new { type = "string", description = "Filter by symbol kind when using symbolName: 'type', 'method', 'property', 'field', 'event'. Helps disambiguate when multiple symbols share a name." },
                                                   filePath = new { type = "string", description = "Absolute path to the file containing the symbol" },
                                                   line = new { type = "integer", description = "1-based line number of the symbol" },
                                                   column = new { type = "integer", description = "1-based column number of the symbol" },
                                                   allDeclarations = new { type = "boolean", description = "Return every declaration (partial classes, overloads, etc.) instead of only the primary one. Default: false." }
                                                },
                                   required = Array.Empty<string>()
                                };

   public object Execute(JObject arguments)
   {
      var (declaredElement, error) = PsiHelpers.ResolveFromArgs(
         _solution,
         arguments["symbolName"]?.ToString(),
         arguments["kind"]?.ToString(),
         arguments["filePath"]?.ToString(),
         arguments["line"]?.Value<int>() ?? 0,
         arguments["column"]?.Value<int>() ?? 0);

      if(error != null) return error;
      using var context = CompilationContexts.For(declaredElement);

      var allDeclarations = arguments["allDeclarations"]?.Value<bool>() ?? false;
      var overloads = Overloads.Of(declaredElement);
      var members = allDeclarations ? overloads : [declaredElement];
      var sources = members.SelectMany(member => MainDeclaration.InOrder(member).Select(it => DeclarationSource.Of(member, it)).OfType<DeclarationSource>())
                           .Take(allDeclarations ? int.MaxValue : 1)
                           .ToList();

      if(sources.Count == 0)
         return DecompiledSource.Of(_solution, declaredElement) is {} decompiled
                   ? Decompiled(declaredElement, decompiled)
                   : $"{PsiHelpers.GetQualifiedName(declaredElement)} — no source declarations (may be source-generated)";

      var answer = new StringBuilder();
      if(members.Count > 1)
         answer.Append(Overloads.Heading(members)).Append("\n\n");
      else if(overloads.Count > 1)
         answer.Append($"One of {overloads.Count} overloads of {PsiHelpers.GetQualifiedName(declaredElement)}: allDeclarations: true gives them all; filePath, line and column pick one.\n\n");

      return answer.Append(string.Join("\n\n", sources.Select(it => it.Text(MaxSourceLength)))).ToString();
   }

   static string Decompiled(IDeclaredElement element, DecompiledSource decompiled)
   {
      var qualifiedName = PsiHelpers.GetQualifiedName(element);
      var text = new StringBuilder();
      text.Append($"{qualifiedName} — {PsiHelpers.AssemblyLabel(element)}").Append('\n');
      if(!Equals(element, decompiled.Type))
         text.Append($"// {element.ShortName} is declared in {PsiHelpers.GetQualifiedName(decompiled.Type)}, shown whole below").Append('\n');

      text.Append(decompiled.Label).Append('\n');
      var source = decompiled.Text;
      if(source.Length > MaxSourceLength)
         source = source.Substring(0, MaxSourceLength) + "\n... (truncated)";

      return text.Append(source).ToString().TrimEnd();
   }
}
