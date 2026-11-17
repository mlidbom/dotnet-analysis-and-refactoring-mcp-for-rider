#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>A namespace <c>browse_namespace</c> was asked for, in the <see cref="JsonFormat">JSON form</see>.</summary>
public sealed class NamespaceJson
{
   /// <summary>Its qualified name, as asked for; empty for the root.</summary>
   [JsonProperty("name")] public string Name { get; private set; } = "";

   /// <summary>The qualified names of the namespaces in it.</summary>
   [JsonProperty("childNamespaces")] public List<string> ChildNamespaces { get; private set; } = [];

   /// <summary>The types in it, not nested in another type.</summary>
   [JsonProperty("types")] public List<TypeJson> Types { get; private set; } = [];

   /// <summary>Why it couldn't be browsed, such as a namespace the solution doesn't have; left out when it was.</summary>
   [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)] public string? Error { get; private set; }

   /// <summary>The elements that failed to be read, and why, when any did; left out otherwise.</summary>
   [JsonProperty("skipped", NullValueHandling = NullValueHandling.Ignore)] public string? Skipped { get; private set; }

   public static NamespaceJson Of(string name, List<string> childNamespaces, List<TypeJson> types, string? skipped) =>
      new() { Name = name, ChildNamespaces = childNamespaces, Types = types, Skipped = skipped };

   public static NamespaceJson NotFound(string name) => new() { Name = name, Error = $"Namespace not found: {name}" };
}
