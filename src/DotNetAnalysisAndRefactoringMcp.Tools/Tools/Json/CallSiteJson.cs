#nullable enable
using DotNetAnalysisAndRefactoringMcp.CallTrees;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>A call with which a caller in <c>get_call_hierarchy</c>'s incoming tree calls the node above, in the <see cref="JsonFormat">JSON form</see>.</summary>
public sealed class CallSiteJson(string? file, GeneratedFile? generatedFile, int line, int column, string code, string? through, bool? asDelegate)
{
   /// <summary>The file's full path; null for a call in code a source generator wrote, whose file has none.</summary>
   [JsonProperty("file")] public string? File { get; } = file;

   /// <summary>For a call in code a source generator wrote, the generator's full type name; left out otherwise.</summary>
   [JsonProperty("generator", NullValueHandling = NullValueHandling.Ignore)] public string? Generator { get; } = generatedFile?.Generator;

   /// <summary>For a call in code a source generator wrote, the name it gave the file the line is in; left out otherwise.</summary>
   [JsonProperty("hintName", NullValueHandling = NullValueHandling.Ignore)] public string? HintName { get; } = generatedFile?.HintName;

   [JsonProperty("line")] public int Line { get; } = line;

   /// <summary>The column of the called member's name.</summary>
   [JsonProperty("column")] public int Column { get; } = column;

   /// <summary>The line's text, trimmed.</summary>
   [JsonProperty("code")] public string Code { get; } = code;

   /// <summary>The qualified name of the member the call names, when the node above overrides or implements it rather than being it.</summary>
   [JsonProperty("through", NullValueHandling = NullValueHandling.Ignore)] public string? Through { get; } = through;

   /// <summary>True when the code passes the node above as a delegate rather than calling it.</summary>
   [JsonProperty("asDelegate", NullValueHandling = NullValueHandling.Ignore)] public bool? AsDelegate { get; } = asDelegate;

   public static CallSiteJson Of(CallSite site) =>
      new(site.GeneratedFile == null ? site.File : null,
          site.GeneratedFile,
          site.Line,
          site.Column,
          site.Code,
          site.Through is { } through ? PsiHelpers.GetQualifiedName(through) : null,
          site.AsDelegate ? true : null);
}
