#nullable enable
using System.Linq;
using JetBrains.ReSharper.Psi;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>
///   Where a symbol is declared, in the <see cref="JsonFormat">JSON form</see>: its file, line and column; for one a
///   source generator wrote, the line and column in the generated file, the generator and the file's hint name; for a
///   symbol compiled into a referenced DLL, the assembly.
/// </summary>
/// <remarks>
///   <para>
///     A member with no declaration of its own is where it's written: a positional record's property at its parameter
///     in the type's header, anything else at its type's name.
///   </para>
///   <para>
///     Until 2026-11-26 a member a source generator wrote into a partial type was at the type's name in the file of its
///     other part, and a wholly generated type nowhere. The form for generated code was agreed with the session
///     symbol-linker, whose linker reads this form, on 2026-11-26.
///   </para>
/// </remarks>
public sealed class DeclarationJson
{
   [JsonProperty("kind")] public string Kind { get; private set; } = "";
   [JsonProperty("name")] public string Name { get; private set; } = "";
   [JsonProperty("qualifiedName")] public string QualifiedName { get; private set; } = "";

   /// <summary>
   ///   Its kind, name, parameters and type as the text answers show them, such as
   ///   <c>method Mix(first:string, second:string) : string</c>: what tells a member's overloads apart, which share
   ///   the rest.
   /// </summary>
   [JsonProperty("signature")] public string Signature { get; private set; } = "";

   /// <summary>The file's full path; null for a symbol a source generator wrote, whose file has none, and a compiled one.</summary>
   [JsonProperty("file")] public string? File { get; private set; }

   /// <summary>The line of its name, in its file or its generated file; null for a compiled symbol.</summary>
   [JsonProperty("line")] public int? Line { get; private set; }

   [JsonProperty("column")] public int? Column { get; private set; }

   /// <summary>The DLL a compiled symbol is in, such as <c>JetBrains.ReSharper.Psi.dll</c>; left out for one in source.</summary>
   [JsonProperty("assembly", NullValueHandling = NullValueHandling.Ignore)] public string? Assembly { get; private set; }

   /// <summary>
   ///   For a symbol a source generator wrote, the generator's full type name, such as
   ///   <c>Shapes.Generators.SwatchGenerator</c>; left out otherwise.
   /// </summary>
   [JsonProperty("generator", NullValueHandling = NullValueHandling.Ignore)] public string? Generator { get; private set; }

   /// <summary>
   ///   For a symbol a source generator wrote, the name it gave the file the line and column are in, such as
   ///   <c>Swatch.g.cs</c>; left out otherwise.
   /// </summary>
   [JsonProperty("hintName", NullValueHandling = NullValueHandling.Ignore)] public string? HintName { get; private set; }

   public static DeclarationJson Of(IDeclaredElement element)
   {
      var site = DeclarationSite.WhereWritten(element);
      return new DeclarationJson
             {
                Kind = JsonNaming.KindOf(element),
                Name = JsonNaming.NameOf(element),
                QualifiedName = JsonNaming.QualifiedNameOf(element),
                Signature = DotNetAnalysisAndRefactoringMcp.Signature.Of(element),
                File = site?.GeneratedFile == null ? site?.File : null,
                Line = site?.Line,
                Column = site?.Column,
                Assembly = site == null ? PsiHelpers.AssemblyFileNameOf(element) : null,
                Generator = site?.GeneratedFile?.Generator,
                HintName = site?.GeneratedFile?.HintName
             };
   }
}
