using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DotNetAnalysisAndRefactoringMcp.Tools.Json;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

public class GoToDefinitionTool : IMcpAlwaysLoadedTool
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
      "A member's name gives every overload of it; a position, the one there. " +
      "Pass multiple symbols via the 'symbols' array to navigate to several in one call. " +
      "With format: 'json', for one symbol: {\"declarations\": [{kind, name, qualifiedName, signature, file, line, column, " +
      "assembly?}], \"truncated\"?: true}, one declaration for a symbol found, one per overload, several for an ambiguous " +
      "name, none for a name nothing matches; 'Type.Member' matches every member whose qualified name ends so. A compiled symbol has the assembly and no " +
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
      return new DeclarationsJson([.. elements.SelectMany(Overloads.Of).Select(DeclarationJson.Of)], found.Truncated);
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

      var members = Overloads.NamedBy(arguments, declaredElement);
      var definitions = members.Select(it => DefinitionOf(it, maxTextLength));
      var answer = new StringBuilder(string.Join("\n\n", members.Count == 1 ? definitions : definitions.Prepend(Overloads.Heading(members))));

      // A compiled member's source is its type's, decompiled, once for all its overloads; get_symbol_source returns all of it.
      if(declaredElement.GetDeclarations().Count == 0 && DecompiledSource.Of(_solution, declaredElement) is {} decompiled)
         answer.Append('\n').Append(decompiled.Label).Append('\n').Append(PsiHelpers.TruncateSnippet(decompiled.Text, maxTextLength));

      return answer.ToString().TrimEnd();
   }

   /// <summary>
   ///   <paramref name="member"/>'s signature, where it is declared, and its declaration's source cut at
   ///   <paramref name="maxTextLength"/>; for a compiled member, its assembly in place of the last two.
   /// </summary>
   static string DefinitionOf(IDeclaredElement member, int maxTextLength)
   {
      var signature = Signature.Of(member);
      if(MainDeclaration.Of(member) is not {} declaration)
         return $"{signature} — {PsiHelpers.AssemblyLabel(member) ?? "no source declarations (may be source-generated)"}";

      var where = DeclarationSite.Of(declaration) is { File.Length: > 0 } site ? $"{site.File}:{site.Line}:{site.Column}" : "[no source]";
      return $"{signature} — {where}\n{PsiHelpers.TruncateSnippet(declaration.GetText(), maxTextLength)}";
   }
}
