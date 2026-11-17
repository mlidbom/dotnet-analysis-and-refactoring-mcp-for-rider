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
   [JsonProperty("file")] public string? File { get; private set; }
   [JsonProperty("line")] public int? Line { get; private set; }
   [JsonProperty("column")] public int? Column { get; private set; }

   /// <summary>The DLL a compiled symbol is in, such as <c>JetBrains.ReSharper.Psi.dll</c>; left out for one in source.</summary>
   [JsonProperty("assembly", NullValueHandling = NullValueHandling.Ignore)] public string? Assembly { get; private set; }

   public static DeclarationJson Of(IDeclaredElement element)
   {
      var site = DeclarationSite.OfFirstDeclarationOf(element) ?? SiteWhereItIsWritten(element);
      return new DeclarationJson
             {
                Kind = JsonNaming.KindOf(element),
                Name = JsonNaming.NameOf(element),
                QualifiedName = JsonNaming.QualifiedNameOf(element),
                File = site?.File,
                Line = site?.Line,
                Column = site?.Column,
                Assembly = site == null ? PsiHelpers.AssemblyFileNameOf(element) : null
             };
   }

   /// <summary>
   ///   Where a member with no declaration of its own is written: a positional record's property, at the parameter of
   ///   the same name in its type's header; anything else, at its type's name.
   /// </summary>
   static DeclarationSite? SiteWhereItIsWritten(IDeclaredElement element)
   {
      if(element is not ITypeMember member || element is ITypeElement || member.ContainingType is not { } type)
         return null;

      return type.Constructors
                 .SelectMany(it => it.Parameters)
                 .Where(it => it.ShortName == member.ShortName)
                 .Select(DeclarationSite.OfFirstDeclarationOf)
                 .FirstOrDefault(it => it != null)
          ?? DeclarationSite.OfFirstDeclarationOf(type);
   }
}
