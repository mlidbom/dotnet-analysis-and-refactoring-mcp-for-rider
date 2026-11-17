#nullable enable
using System.Collections.Generic;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary><c>browse_namespace</c>'s answer in the <see cref="JsonFormat">JSON form</see>: one namespace for each asked for, the root when none was.</summary>
public sealed class NamespacesJson(List<NamespaceJson> namespaces)
{
   [JsonProperty("namespaces")] public List<NamespaceJson> Namespaces { get; } = namespaces;
}
