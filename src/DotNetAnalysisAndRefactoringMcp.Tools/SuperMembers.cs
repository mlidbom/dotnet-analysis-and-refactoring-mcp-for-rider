#nullable enable
using System.Collections.Generic;
using System.Linq;
using JetBrains.ReSharper.Psi;

namespace DotNetAnalysisAndRefactoringMcp
{
  /// <summary>
  ///   The members a member overrides or implements: the interface and base members that a call can reach it through.
  /// </summary>
  /// <remarks>
  ///   Found by ReSharper (<see cref="OverridableMemberExtensions.GetAllSuperMembers"/>), so they include implicit and
  ///   explicit implementations, overridden base members, and quasi-implementations, where a base class's member
  ///   implements an interface on a derived class's behalf.
  ///   Compiled ones are kept apart: following them floods the answer. <c>IDisposable.Dispose</c>, for example, is
  ///   referenced by every <c>using</c> in the solution.
  /// </remarks>
  public sealed class SuperMembers
  {
    /// <summary>The ones declared in the solution's sources, whose references a search follows.</summary>
    public IReadOnlyList<IDeclaredElement> InSource { get; }

    /// <summary>The ones compiled into referenced assemblies, which a search names but doesn't follow.</summary>
    public IReadOnlyList<IDeclaredElement> Compiled { get; }

    SuperMembers(IReadOnlyList<IDeclaredElement> inSource, IReadOnlyList<IDeclaredElement> compiled)
    {
      InSource = inSource;
      Compiled = compiled;
    }

    public static SuperMembers Of(IDeclaredElement element)
    {
      if (!(element is IOverridableMember member))
        return new SuperMembers([], []);

      var all = member.GetAllSuperMembers(searchQuasi: true)
                      .Select(it => (IDeclaredElement)it.Member)
                      .Where(it => !it.Equals(element))
                      .Distinct()
                      .ToList();

      return new SuperMembers([..all.Where(IsInSource)], [..all.Where(it => !IsInSource(it))]);
    }

    /// <summary>
    ///   A line saying which were followed and which weren't, such as "(includes calls through Shapes.IShape.Area)",
    ///   or nothing when there are none. <paramref name="what"/> is what was searched for: "calls", "usages".
    /// </summary>
    public string? Line(string what)
    {
      var parts = new List<string>();
      if (InSource.Count > 0)
        parts.Add($"includes {what} through {Names(InSource)}");
      if (Compiled.Count > 0)
        parts.Add($"not {what} through the compiled {Names(Compiled)}: ask about that by name for those");

      return parts.Count == 0 ? null : "(" + string.Join("; ", parts) + ")";
    }

    static string Names(IEnumerable<IDeclaredElement> members) =>
      string.Join(", ", members.Select(PsiHelpers.GetQualifiedName));

    static bool IsInSource(IDeclaredElement element) => element.GetSourceFiles().Any();
  }
}
