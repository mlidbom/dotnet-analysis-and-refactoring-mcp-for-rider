#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.ReSharper.Psi;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   The code a call tree follows calls into: by default the solution's own source, which the arguments narrow to
///   namespaces, <c>namespaces: ["Shapes.Drawing"]</c>, or widen to compiled assemblies, <c>assemblies: ["System.Linq"]</c>.
/// </summary>
/// <remarks>
///   Every call into a compiled assembly, such as <c>List.Add</c> or <c>StringBuilder.Append</c>, was a node of the
///   outgoing tree until 2026-11-24: for <c>GetSymbolSourceTool.Execute</c> at depth 2, 32 of its 48 nodes. A call
///   gives the scope rather than a setting kept for the session: the server keeps nothing per client, and an answer
///   that depends on an earlier call is misread once that call is compacted away.
/// </remarks>
public sealed class CallScope
{
   readonly IReadOnlyList<string> _namespaces;
   readonly IReadOnlyList<string> _assemblies;

   CallScope(IReadOnlyList<string> namespaces, IReadOnlyList<string> assemblies)
   {
      _namespaces = namespaces;
      _assemblies = assemblies;
   }

   public static CallScope From(JObject arguments) => new(NamesIn(arguments["namespaces"]), NamesIn(arguments["assemblies"]));

   /// <summary>The <c>namespaces</c> argument, as a tool's input schema describes it.</summary>
   public static object NamespacesArgument => new
                                              {
                                                 type = "array",
                                                 items = new { type = "string" },
                                                 description = "Follow only calls into these namespaces and the ones inside them, such as 'MyApp.Core'. Default: every namespace."
                                              };

   /// <summary>The <c>assemblies</c> argument, as a tool's input schema describes it.</summary>
   public static object AssembliesArgument => new
                                              {
                                                 type = "array",
                                                 items = new { type = "string" },
                                                 description = "Also show calls into these compiled assemblies, by name without '.dll', such as 'Compze.Threading'; a name covers the assemblies whose names start with it and a dot. Default: none, only the solution's own code."
                                              };

   /// <summary>Whether a call into <paramref name="callee"/> is followed.</summary>
   public bool Contains(Callee callee) =>
      (callee.IsInSource || IsInANamedAssembly(callee)) && IsInTheNamespaces(callee);

   /// <summary>What it is, for the heading of an answer: "the solution's code", "the solution's code and System.Linq, in Shapes.Drawing".</summary>
   public string Description =>
      "the solution's code"
    + (_assemblies.Count > 0 ? $" and {string.Join(", ", _assemblies)}" : "")
    + (_namespaces.Count > 0 ? $", in {string.Join(", ", _namespaces)}" : "");

   /// <summary>The namespaces it is narrowed to, as <see cref="CallsNotShown.Line"/> names them.</summary>
   public string Namespaces => string.Join(", ", _namespaces);

   /// <summary>Whether the <c>assemblies</c> argument names <paramref name="assembly"/>, or an assembly whose name starts it.</summary>
   public bool Names(string assembly) => _assemblies.Any(it => IsTheSameOrInside(assembly, it));

   bool IsInANamedAssembly(Callee callee) => callee.AssemblyName is { } assembly && Names(assembly);

   /// <summary>
   ///   A callee whose namespace isn't known, such as a local function, is in them: it is in the code of a member
   ///   that was followed.
   /// </summary>
   bool IsInTheNamespaces(Callee callee) =>
      _namespaces.Count == 0 || NamespaceOf(callee.Member) is not { } name || _namespaces.Any(it => IsTheSameOrInside(name, it));

   static string? NamespaceOf(IDeclaredElement element) =>
      element switch
      {
         ITypeElement type          => type.GetContainingNamespace().QualifiedName,
         IClrDeclaredElement member => member.GetContainingType() is { } type ? NamespaceOf(type) : null,
         _                          => null
      };

   static bool IsTheSameOrInside(string name, string outer) =>
      string.Equals(name, outer, StringComparison.OrdinalIgnoreCase)
   || name.StartsWith(outer + ".", StringComparison.OrdinalIgnoreCase);

   static IReadOnlyList<string> NamesIn(JToken? argument) =>
      argument is JArray names
         ? [.. names.Select(it => it.ToString().Trim()).Where(it => it.Length > 0)]
         : [];
}
