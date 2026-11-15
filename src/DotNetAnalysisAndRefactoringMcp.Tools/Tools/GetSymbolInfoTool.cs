using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

public class GetSymbolInfoTool : IMcpTool
{
   readonly ISolution _solution;

   public GetSymbolInfoTool(ISolution solution) => _solution = solution;

   public string Name => "get_symbol_info";

   public string Description =>
      "Get detailed information about a code symbol: " +
      "kind, full qualified name, type, documentation, containing type/namespace, and parameter info for methods. " +
      "Provide either a symbolName or a file path with position. " +
      "Pass multiple symbols via the 'symbols' array to query several in one call.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   symbolName = new { type = "string", description = "Symbol name to get info about (e.g. 'MyClass', 'Namespace.MyClass'). Alternative to filePath+line+column." },
                                                   kind = new { type = "string", description = "Filter by symbol kind when using symbolName: 'type', 'method', 'property', 'field', 'event'. Helps disambiguate when multiple symbols share a name." },
                                                   includeMembers = new { type = "boolean", description = "If true, include the type's members (methods, properties, fields, events) in the response. Only applies to type symbols. Default: false." },
                                                   filePath = new { type = "string", description = "Absolute path to the file containing the symbol" },
                                                   line = new { type = "integer", description = "1-based line number of the symbol" },
                                                   column = new { type = "integer", description = "1-based column number of the symbol" },
                                                   symbols = new
                                                             {
                                                                type = "array",
                                                                description = "Array of symbols to get info for in batch. Each item is an object with symbolName/kind or filePath/line/column. Results are concatenated with separators.",
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
      ToolBatch.Of(arguments, "symbols", item: "symbolName")?.Run(ExecuteSingle) ?? ExecuteSingle(arguments);

   object ExecuteSingle(JObject arguments)
   {
      var (declaredElement, resolveError) = PsiHelpers.ResolveFromArgs(
         _solution,
         arguments["symbolName"]?.ToString(),
         arguments["kind"]?.ToString(),
         arguments["filePath"]?.ToString(),
         arguments["line"]?.Value<int>() ?? 0,
         arguments["column"]?.Value<int>() ?? 0);

      if(resolveError != null) return resolveError;
      using var context = CompilationContexts.For(declaredElement);

      var includeMembers = arguments["includeMembers"]?.Value<bool>() ?? false;
      var lang = declaredElement.PresentationLanguage ?? CSharpLanguage.Instance;
      var sb = new StringBuilder();

      // Header: compact signature
      sb.AppendLine(PsiHelpers.FormatSignature(declaredElement));

      // Containing type / namespace
      if(declaredElement is IClrDeclaredElement clrElement)
      {
         var containingType = clrElement.GetContainingType();
         if(containingType != null)
            sb.Append("containingType: ").AppendLine(containingType.GetClrName().FullName);

         if(declaredElement is ITypeElement typeElem)
         {
            var ns = typeElem.GetContainingNamespace();
            if(ns != null && !string.IsNullOrEmpty(ns.QualifiedName))
               sb.Append("namespace: ").AppendLine(ns.QualifiedName);
         } else if(containingType != null)
         {
            var ns = containingType.GetContainingNamespace();
            if(ns != null && !string.IsNullOrEmpty(ns.QualifiedName))
               sb.Append("namespace: ").AppendLine(ns.QualifiedName);
         }
      }

      // Base types for type elements
      if(declaredElement is ITypeElement typeElement)
      {
         // An interface's super types include object, as the CLR has it, though C# lists no base for it.
         var superTypes = typeElement.GetSuperTypes()
                                     .Where(t => !(typeElement is IInterface && t.GetClrName().FullName == "System.Object"))
                                     .Select(t => t.GetPresentableName(lang))
                                     .ToList();
         if(superTypes.Any())
            sb.Append("baseTypes: ").AppendLine(string.Join(", ", superTypes));
      }

      // Declaration location
      var site = DeclarationSite.OfFirstDeclarationOf(declaredElement);
      if(site != null)
         sb.Append("declared: ").Append(site.File).Append(':').Append(site.Line).Append(':').AppendLine(site.Column.ToString());

      AppendXmlDocumentation(sb, declaredElement);

      // Members (when requested for type symbols)
      if(includeMembers && declaredElement is ITypeElement membersType)
      {
         // Collect property/event names to filter out compiler-generated accessors
         var propertyNames = new HashSet<string>();
         var eventNames = new HashSet<string>();
         foreach(var m in membersType.GetMembers())
         {
            if(m is IProperty prop) propertyNames.Add(prop.ShortName);
            if(m is IEvent evt) eventNames.Add(evt.ShortName);
         }

         sb.AppendLine();
         sb.AppendLine("members:");

         foreach(var member in membersType.GetMembers())
         {
            // Skip compiler-generated accessors (get_X/set_X for properties, add_X/remove_X for events)
            if(member is IMethod accessorMethod)
            {
               var name = accessorMethod.ShortName;
               if((name.StartsWith("get_") || name.StartsWith("set_")) &&
                  propertyNames.Contains(name.Substring(4)))
                  continue;
               if((name.StartsWith("add_") || name.StartsWith("remove_")) &&
                  eventNames.Contains(name.Substring(name.IndexOf('_') + 1)))
                  continue;
            }

            // Skip compiler-generated record members
            if(IsCompilerGeneratedMember(member, membersType))
               continue;

            sb.Append("  ").Append(AccessOf(member)).AppendLine(PsiHelpers.FormatSignature(member));
         }
      }

      return sb.ToString().TrimEnd();
   }

   /// <summary>
   /// "private " and the like, as C# writes it, before a member in the listing: a compiled type's listing holds its
   /// private members too, and nothing else tells them from the public ones.
   /// </summary>
   static string AccessOf(ITypeMember member) => member.GetAccessRights() switch
   {
      AccessRights.PUBLIC                 => "public ",
      AccessRights.PRIVATE                => "private ",
      AccessRights.INTERNAL               => "internal ",
      AccessRights.PROTECTED              => "protected ",
      AccessRights.PROTECTED_OR_INTERNAL  => "protected internal ",
      AccessRights.PROTECTED_AND_INTERNAL => "private protected ",
      _                                   => ""
   };

   static void AppendXmlDocumentation(StringBuilder sb, IDeclaredElement declaredElement)
   {
      var xmlDoc = TryGetXmlDoc(declaredElement);
      var summary = xmlDoc?.SelectSingleNode("//summary")?.InnerText?.Trim();
      var remarks = xmlDoc?.SelectSingleNode("//remarks")?.InnerText?.Trim();

      // Some static methods trigger an internal NRE in ReSharper's XML-doc API.
      // Fall back to source comments so symbol info still returns useful metadata.
      if(string.IsNullOrEmpty(summary) && string.IsNullOrEmpty(remarks))
      {
         var fallback = TryReadXmlDocumentationFromSource(declaredElement);
         summary = fallback.summary;
         remarks = fallback.remarks;
      }

      if(!string.IsNullOrEmpty(summary))
         sb.Append("doc: ").AppendLine(summary);

      if(!string.IsNullOrEmpty(remarks))
         sb.Append("remarks: ").AppendLine(remarks);
   }

   /// <remarks>The NRE is ReSharper's, observed for some static methods: their doc is then read from the source.</remarks>
   static XmlNode TryGetXmlDoc(IDeclaredElement declaredElement)
   {
      try
      {
         return declaredElement.GetXMLDoc(true);
      }
      catch(NullReferenceException)
      {
         return null;
      }
   }

   static (string summary, string remarks) TryReadXmlDocumentationFromSource(IDeclaredElement declaredElement)
   {
      var declaration = declaredElement.GetDeclarations().FirstOrDefault();
      if(declaration == null)
         return default;

      var site = DeclarationSite.Of(declaration);
      var filePath = site?.File;
      if(string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
         return default;

      // Up from the name's line, over attributes, to the doc comment.
      var declLine = site.Line;
      if(declLine <= 1)
         return default;

      var lines = File.ReadAllLines(filePath);
      if(declLine - 1 > lines.Length)
         return default;

      var docLines = new List<string>();
      for(var lineIndex = declLine - 2; lineIndex >= 0; lineIndex--)
      {
         var trimmed = lines[lineIndex].TrimStart();
         if(trimmed.StartsWith("///"))
         {
            docLines.Insert(0, trimmed.Substring(3));
            continue;
         }

         if(string.IsNullOrWhiteSpace(trimmed) || (docLines.Count == 0 && trimmed.StartsWith("[")))
            continue;

         break;
      }

      if(docLines.Count == 0)
         return default;

      try
      {
         var document = XDocument.Parse("<root>" + string.Join(Environment.NewLine, docLines) + "</root>");
         var root = document.Root;
         if(root == null)
            return default;

         return (
                   root.Element("summary")?.Value?.Trim(),
                   root.Element("remarks")?.Value?.Trim());
      }
      catch(XmlException)
      {
         // A doc comment that isn't well-formed XML, such as one with a bare '<': its text as written, and saying so.
         return (string.Join(" ", docLines).Trim() + " [the doc comment isn't well-formed XML]", null);
      }
   }

   /// <summary>
   /// Detects compiler-generated members typical of records: Equals, GetHashCode, ToString,
   /// PrintMembers, Deconstruct, op_Equality, op_Inequality, EqualityContract, and clone methods.
   /// </summary>
   static bool IsCompilerGeneratedMember(ITypeMember member, ITypeElement containingType)
   {
      var name = member.ShortName;

      // Skip any $-prefixed members (compiler internals)
      if(name.StartsWith("$") || name.StartsWith("<"))
         return member.GetDeclarations().Count == 0;

      // Equality operators are always compiler-generated on records
      // (these may be IOperator, not IMethod, so check before the IMethod block)
      if(name == "op_Equality" || name == "op_Inequality")
         return true;

      // Parameterless constructor on a record struct (always compiler-generated)
      if(name == ".ctor" && member is IParametersOwner ctor && ctor.Parameters.Count == 0)
         return true;

      if(member is IMethod)
      {
         switch(name)
         {
            case "Equals":
            case "GetHashCode":
            case "ToString":
            case "PrintMembers":
            case "Deconstruct":
               return member.GetDeclarations().Count == 0;
         }
      }

      // EqualityContract property (record-generated)
      if(member is IProperty && name == "EqualityContract")
         return member.GetDeclarations().Count == 0;

      return false;
   }
}
