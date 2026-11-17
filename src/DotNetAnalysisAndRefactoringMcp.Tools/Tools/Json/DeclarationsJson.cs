#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>
///   <c>go_to_definition</c>'s answer in the <see cref="JsonFormat">JSON form</see>: one declaration for a symbol found,
///   several for an ambiguous name, none for a name nothing matches.
/// </summary>
public sealed class DeclarationsJson(List<DeclarationJson> declarations, bool truncated)
{
   [JsonProperty("declarations")] public List<DeclarationJson> Declarations { get; } = declarations;

   /// <summary>True when a search for an unqualified member name stopped at its cap: there may be more.</summary>
   [JsonProperty("truncated", NullValueHandling = NullValueHandling.Ignore)] public bool? Truncated { get; } = truncated ? true : null;
}
