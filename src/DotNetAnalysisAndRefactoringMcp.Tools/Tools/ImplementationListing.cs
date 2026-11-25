#nullable enable
using System.Collections.Generic;
using System.Linq;
using JetBrains.ReSharper.Psi;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   What <c>find_implementations</c> answers for one type or member: the <see cref="Implementation"/>s it found of it,
///   whether there were more past <c>maxResults</c>, and the elements it skipped.
/// </summary>
sealed class ImplementationListing
{
   readonly IDeclaredElement _implemented;
   readonly List<Implementation> _implementations = [];

   /// <param name="implemented">The type or member asked about.</param>
   public ImplementationListing(IDeclaredElement implemented) => _implemented = implemented;

   /// <summary>The elements ReSharper failed to read while the search went through them, for the listing's last line.</summary>
   public SkippedElements Skipped { get; } = new();

   /// <summary>Whether the search stopped at <c>maxResults</c> with more to find.</summary>
   public bool HasMorePastMaxResults { get; set; }

   public int Count => _implementations.Count;

   public void Add(Implementation implementation) => _implementations.Add(implementation);

   /// <summary>
   ///   The <see cref="Heading"/>, a line for each <see cref="Implementation"/>, <see cref="InOrder"/>, and last the
   ///   <see cref="Skipped"/> elements' line, if any.
   /// </summary>
   public string Text(SolutionRoot root)
   {
      var lines = new List<string> { Heading };
      if(Count > 0)
         lines.AddRange(["", .. InOrder.Select(it => it.Text(root))]);

      if(Skipped.Line is {} skipped)
         lines.AddRange(["", skipped]);

      return string.Join("\n", lines);
   }

   /// <summary>The signature of what was asked about, and how many implement it.</summary>
   string Heading => $"{Signature.Of(_implemented)} — {HowMany}{(HasMorePastMaxResults ? ", and more past maxResults" : "")}";

   string HowMany => Count == 1 ? "1 implementation" : $"{Count} implementations";

   /// <summary>Those that name what was asked about directly first, then by name.</summary>
   IEnumerable<Implementation> InOrder => _implementations.OrderBy(it => it.IsDirect ? 0 : 1).ThenBy(it => it.Name);
}
