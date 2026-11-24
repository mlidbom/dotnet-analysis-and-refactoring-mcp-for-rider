#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace DotNetAnalysisAndRefactoringMcp.CallTrees;

/// <summary>
///   The calls a call tree left out, being outside its <see cref="CallScope"/>: those into compiled code counted by
///   assembly, so that the answer says what <c>assemblies</c> would add, and those into the solution's code outside the
///   namespaces it was narrowed to.
/// </summary>
public sealed class CallsNotShown(CallScope scope)
{
   const int MostAssembliesNamed = 5;

   readonly Dictionary<string, int> _intoAssemblies = new(StringComparer.OrdinalIgnoreCase);

   /// <summary>Calls into the solution's code, or into an assembly the scope names, outside the namespaces it is narrowed to.</summary>
   public int OutsideTheNamespaces { get; private set; }

   /// <summary>Calls into compiled assemblies the scope doesn't name, by assembly.</summary>
   public IReadOnlyDictionary<string, int> IntoAssemblies => _intoAssemblies;

   /// <summary>Counts a call into <paramref name="callee"/>, which the scope doesn't contain; one the compiler makes up, such as the <c>+</c> of two integers, isn't counted.</summary>
   public void Add(Callee callee)
   {
      var assembly = callee.AssemblyName;
      if(assembly != null && !scope.Names(assembly))
         _intoAssemblies[assembly] = _intoAssemblies.TryGetValue(assembly, out var count) ? count + 1 : 1;
      else if(assembly != null || callee.IsInSource)
         OutsideTheNamespaces++;
   }

   /// <summary>
   ///   "Not shown: 7 calls into compiled code, in System.Linq (4), System.Runtime (3): 'assemblies' adds them.", or
   ///   nothing when every call was shown.
   /// </summary>
   public string? Line()
   {
      var parts = new List<string>();
      if(_intoAssemblies.Count > 0)
         parts.Add($"{Calls(_intoAssemblies.Values.Sum())} into compiled code, in {Assemblies()}: 'assemblies' adds them");
      if(OutsideTheNamespaces > 0)
         parts.Add($"{Calls(OutsideTheNamespaces)} outside {scope.Namespaces}");

      return parts.Count == 0 ? null : $"Not shown: {string.Join("; ", parts)}.";
   }

   string Assemblies()
   {
      var byCount = _intoAssemblies.OrderByDescending(it => it.Value).ThenBy(it => it.Key, StringComparer.Ordinal).ToList();
      var named = string.Join(", ", byCount.Take(MostAssembliesNamed).Select(it => $"{it.Key} ({it.Value})"));
      return byCount.Count > MostAssembliesNamed ? $"{named} and {byCount.Count - MostAssembliesNamed} more" : named;
   }

   static string Calls(int count) => count == 1 ? "1 call" : $"{count} calls";
}
