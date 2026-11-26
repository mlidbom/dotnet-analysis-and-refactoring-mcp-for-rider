#nullable enable
using System.Linq;
using JetBrains.ReSharper.Psi;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>
///   Where a symbol is declared, in the <see cref="JsonFormat">JSON form</see>: its file, line and column, or for a
///   symbol compiled into a referenced DLL, the assembly.
/// </summary>
/// <remarks>
///   A member with no declaration of its own is where it's written: a positional record's property at its parameter in
///   the type's header, anything else at its type's name.
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

   [JsonProperty("file")] public string? File { get; private set; }
   [JsonProperty("line")] public int? Line { get; private set; }
   [JsonProperty("column")] public int? Column { get; private set; }

   /// <summary>The DLL a compiled symbol is in, such as <c>JetBrains.ReSharper.Psi.dll</c>; left out for one in source.</summary>
   [JsonProperty("assembly", NullValueHandling = NullValueHandling.Ignore)] public string? Assembly { get; private set; }

   public static DeclarationJson Of(IDeclaredElement element)
   {
      //todo: DeclarationSite.WhereWritten, with a generated member at its own generated file rather than at its partial
      //   type's name, once the session symbol-linker has a specification for it: the symbol linker reads this form
      //   (optimize-royder, 2026-11-26). Its declarations then need to say which generated file, as the text answers do.
      var site = DeclarationSite.WhereWrittenInAFileWithAPath(element);
      return new DeclarationJson
             {
                Kind = JsonNaming.KindOf(element),
                Name = JsonNaming.NameOf(element),
                QualifiedName = JsonNaming.QualifiedNameOf(element),
                Signature = DotNetAnalysisAndRefactoringMcp.Signature.Of(element),
                File = site?.File,
                Line = site?.Line,
                Column = site?.Column,
                Assembly = site == null ? PsiHelpers.AssemblyFileNameOf(element) : null
             };
   }
}
