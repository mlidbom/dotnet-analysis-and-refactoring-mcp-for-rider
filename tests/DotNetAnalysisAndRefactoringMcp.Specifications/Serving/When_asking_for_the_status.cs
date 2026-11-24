using System.Diagnostics;
using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

/// <summary>
///   <c>internal/status</c>, by which the Riders' servers learn each other's role and solutions, and the standalone
///   primary learns which of its private Rider's backends serves which solution.
/// </summary>
public class When_asking_for_the_status(TheSandboxWithTheFixtureSolution sandbox)
{
   readonly JsonElement _status = sandbox.McpServer.Status();

   [XF] public void it_names_the_rider_backend_process_that_answers()
   {
      using var answering = Process.GetProcessById(_status.GetProperty("pid").GetInt32());
      answering.ProcessName.Must().Be("Rider.BackendHost");
   }

   [XF] public void it_lists_the_fixture_solution_with_its_path_as_a_local_one() =>
      _status.GetProperty("solutions").EnumerateArray()
             .Select(it => new { Path = it.GetProperty("path").GetString()!, Local = it.GetProperty("local").GetBoolean() }).ToList()
             .Must().Satisfy(rows => rows.Any(row => row.Local && row.Path.EndsWith(TheSandboxWithTheFixtureSolution.SolutionFile)));
}
