using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DotNetAnalysisAndRefactoringMcp.Tools.Json;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

public class GoToDefinitionTool : IMcpTool
{
   const int DefaultSourceTextLength = 200;
   readonly ISolution _solution;

   public GoToDefinitionTool(ISolution solution) => _solution = solution;

   public string Name => "go_to_definition";
   public ToolAnnotations Annotations => ToolAnnotations.ReadOnly;

   public string Description =>
      "Navigate to the definition/declaration of a symbol. " +
      "Given a usage site (file+line+column) or a symbol name, returns the file path and position " +
      "where the symbol is declared, along with the declaration source text. " +
      "For a symbol compiled into a referenced DLL, returns its assembly and decompiled source instead. " +
      "Pass multiple symbols via the 'symbols' array to navigate to several in one call. " +
      "With format: 'json', for one symbol: {\"declarations\": [{kind, name, qualifiedName, file, line, column, assembly?}], " +
      "\"truncated\"?: true}, one declaration for a symbol found, several for an ambiguous name, none for a name nothing " +
      "matches; 'Type.Member' matches every member whose qualified name ends so. A compiled symbol has the assembly and no " +
      "file; a member with no source of its own, such as a positional record's property, is where its type is declared.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   symbolName = new { type = "string", description = "Symbol name to find the definition of (e.g. 'MyClass', 'Namespace.MyClass'). Alternative to filePath+line+column." },
                                                   kind = new { type = "string", description = "Filter by symbol kind when using symbolName: 'type', 'method', 'property', 'field', 'event'. Helps disambiguate when multiple symbols share a name." },
                                                   maxTextLength = new { type = "integer", @default = DefaultSourceTextLength, description = $"Maximum source characters in the text reply. Default: {DefaultSourceTextLength}. Set higher for a longer declaration." },
                                                   filePath = new { type = "string", description = "Absolute path to the file containing a usage of the symbol" },
                                                   line = new { type = "integer", description = "1-based line number of the symbol usage" },
                                                   column = new { type = "integer", description = "1-based column number of the symbol usage" },
                                                   format = JsonFormat.Argument,
                                                   symbols = new
                                                             {
                                                                type = "array",
                                                                description = "Array of symbols to navigate to in batch. Each item is an object with symbolName/kind or filePath/line/column. Results are concatenated with separators.",
                                                                items = new
                                                                        {
                                                                           type = "object",
                                                                           properties = new
                                                                                        {
                                                                                           symbolName = new { type = "string", description = "Symbol name" },
                                                                                           kind = new { type = "string", description = "Symbol kind filter" },
                                                                                           filePath = new { type = "string", description = "File path" },
                                                                                           line = new { type = "integer", description = "1-based line" },
                                                                                           column = new { type = "integer", description = "1-based column" }
                                                                                        }
                                                                        }
                                                             }
                                                },
                                   required = Array.Empty<string>()
                                };

   public object Execute(JObject arguments) =>
      JsonFormat.IsAskedFor(arguments)
         ? DeclarationsOf(arguments)
         : ToolBatch.Of(arguments, "symbols", item: "symbolName")?.Run(ExecuteSingle) ?? ExecuteSingle(arguments);

   /// <summary>The JSON form: every declaration the name or position stands for.</summary>
   object DeclarationsOf(JObject arguments)
   {
      if(arguments["symbols"] != null)
         return new { error = "format 'json' takes one symbol: a symbolName, or a filePath with line and column" };

      var symbolName = arguments["symbolName"]?.ToString();
      if(string.IsNullOrEmpty(symbolName))
      {
         var (element, error) = PsiHelpers.ResolveFromArgs(_solution,
                                                           null,
                                                           arguments["kind"]?.ToString(),
                                                           arguments["filePath"]?.ToString(),
                                                           arguments["line"]?.Value<int>() ?? 0,
                                                           arguments["column"]?.Value<int>() ?? 0);
         return error ?? new DeclarationsJson([DeclarationJson.Of(element)], truncated: false);
      }

      var found = PsiHelpers.ResolveSymbolByName(_solution, symbolName, arguments["kind"]?.ToString());
      IEnumerable<IDeclaredElement> elements = found.IsFound ? [found.Element] : found.Candidates?.Select(it => it.Element) ?? [];
      return new DeclarationsJson([.. elements.Select(DeclarationJson.Of)], found.Truncated);
   }

   object ExecuteSingle(JObject arguments)
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

      var maxTextLength = arguments["maxTextLength"]?.Value<int>() ?? DefaultSourceTextLength;
      if(maxTextLength <= 0) maxTextLength = DefaultSourceTextLength;
      if(maxTextLength > 50000) maxTextLength = 50000;

      var declarations = declaredElement.GetDeclarations();
      if(declarations.Count == 0)
      {
         var signature = PsiHelpers.FormatSignature(declaredElement);
         var assembly = PsiHelpers.AssemblyLabel(declaredElement);
         if(assembly == null)
            return new { error = $"{signature}: no source declarations (may be source-generated)" };

         // Compiled: the decompiled type stands in for the declaration; get_symbol_source returns all of it.
         var decompiled = DecompiledSource.Of(_solution, declaredElement);
         var body = decompiled == null
                       ? ""
                       : $"\n{decompiled.Label}\n{PsiHelpers.TruncateSnippet(decompiled.Text, maxTextLength)}";

         return $"{signature} — {assembly}{body}";
      }

      var decl = declarations[0];
      var site = DeclarationSite.Of(decl);
      if(site == null)
         return new { error = "Could not resolve declaration location" };

      var declFilePath = site.File.Length > 0 ? site.File : "[no source]";
      var (declLine, declCol) = (site.Line, site.Column);

      var sb = new StringBuilder();
      sb.Append(PsiHelpers.FormatSignature(declaredElement));
      sb.Append(" — ").Append(declFilePath);
      sb.Append(':').Append(declLine).Append(':').AppendLine(declCol.ToString());
      sb.Append(PsiHelpers.TruncateSnippet(decl.GetText(), maxTextLength));

      return sb.ToString().TrimEnd();
   }
}
