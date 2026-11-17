using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;
using Xunit;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithAPeer;

[Collection(nameof(WithThePeerRider))]
public class When_listing_the_open_solutions(TheSandboxWithTheFixtureSolution sandbox, ThePeerRider peer)
{
   readonly IReadOnlyList<JsonElement> _solutions = sandbox.McpServer.ListSolutions().GetProperty("solutions").EnumerateArray().ToList();
   readonly string _thisRidersSolution = sandbox.PathOf(TheSandboxWithTheFixtureSolution.SolutionFile);
   readonly string _peersSolution = peer.SolutionPath;

   IReadOnlyList<string> Paths => _solutions.Select(it => it.GetProperty("path").GetString()!).ToList();

   [XF] public void it_lists_this_Riders_solution() => Paths.Must().Satisfy(it => it.Contains(_thisRidersSolution, StringComparer.OrdinalIgnoreCase));

   [XF] public void it_lists_the_solution_the_peer_registered() => Paths.Must().Satisfy(it => it.Contains(_peersSolution, StringComparer.OrdinalIgnoreCase));

   /// <summary>Both are copies of the fixture solution, so they share its name.</summary>
   [XF] public void it_gives_each_of_the_two_a_path_segment_of_its_own_to_name_it_by() =>
      _solutions.Select(it => it.TryGetProperty("uniquePathSegment", out var segment) ? segment.GetString() : null)
                .ToList()
                .Must().Satisfy(it => it.Count == 2 && it.All(segment => segment != null) && it.Distinct().Count() == 2);
}
