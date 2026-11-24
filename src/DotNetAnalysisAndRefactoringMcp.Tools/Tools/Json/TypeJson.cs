#nullable enable
using System.Collections.Generic;
using System.Linq;
using JetBrains.ReSharper.Psi;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>A type in the <see cref="JsonFormat">JSON form</see>: where it's declared, its nested types, and on request its members.</summary>
public sealed class TypeJson
{
   [JsonProperty("kind")] public string Kind { get; private set; } = "";
   [JsonProperty("name")] public string Name { get; private set; } = "";
   [JsonProperty("qualifiedName")] public string QualifiedName { get; private set; } = "";
   [JsonProperty("file")] public string? File { get; private set; }
   [JsonProperty("line")] public int? Line { get; private set; }
   [JsonProperty("column")] public int? Column { get; private set; }

   /// <summary>Its declared members, when the call asked for them; left out otherwise.</summary>
   [JsonProperty("members", NullValueHandling = NullValueHandling.Ignore)] public List<MemberJson>? Members { get; private set; }

   /// <summary>The types declared in it, each with members of its own when the call asked for them.</summary>
   [JsonProperty("nestedTypes")] public List<TypeJson> NestedTypes { get; private set; } = [];

   public static TypeJson Of(ITypeElement type, bool withMembers)
   {
      var site = DeclarationSite.OfMainDeclarationOf(type);
      return new TypeJson
             {
                Kind = JsonNaming.KindOf(type),
                Name = JsonNaming.NameOf(type),
                QualifiedName = JsonNaming.QualifiedNameOf(type),
                File = site?.File,
                Line = site?.Line,
                Column = site?.Column,
                Members = withMembers ? MemberJson.OfTheMembersOf(type) : null,
                NestedTypes = [.. type.NestedTypes.Where(it => it is not ITypeExtension).OrderBy(it => it.ShortName).Select(it => Of(it, withMembers))]
             };
   }
}
