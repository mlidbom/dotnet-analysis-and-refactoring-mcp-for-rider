using System.Text.Json;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Swapping;

/// <summary>
///   A new tools build deployed while the sandbox runs, twice: each is served from the next call on, with no restart,
///   and the build it replaced is unloaded and freed.
/// </summary>
public class When_the_tools_are_swapped_twice
{
   static readonly TimeSpan HowLongFreeingMayTake = TimeSpan.FromSeconds(40);

   readonly string _newest;
   readonly string _answer;
   readonly JsonElement _tools;

   public When_the_tools_are_swapped_twice(TheSandboxWithTheFixtureSolution sandbox)
   {
      sandbox.DeployTheToolsAgain();
      sandbox.McpServer.CallTool("search_symbol", new { query = "Circle" });
      _newest = sandbox.DeployTheToolsAgain();
      _answer = sandbox.McpServer.CallTool("search_symbol", new { query = "Circle" });
      _tools = OnceNoneIsBeingFreed(() => sandbox.McpServer.ToolsOf(sandbox.PathOf("FixtureSolution.slnx")));
   }

   [XF] public void it_answers_from_the_newest_build() => _tools.GetProperty("build").GetString().Must().Be(_newest);

   [XF] public void it_still_answers() => _answer.Must().Contain("Circle");

   /// <summary>Every build swapped out in this run, the first included, which ran every tool first in its process.</summary>
   [XF] public void it_frees_every_build_it_swapped_out() => _tools.GetProperty("notFreed").GetArrayLength().Must().Be(0);

   static JsonElement OnceNoneIsBeingFreed(Func<JsonElement> tools)
   {
      var deadline = DateTime.UtcNow + HowLongFreeingMayTake;
      while(true)
      {
         var status = tools();
         if(status.GetProperty("beingFreed").GetArrayLength() == 0) return status;
         if(DateTime.UtcNow > deadline) throw new TimeoutException($"Builds were still being freed after {HowLongFreeingMayTake}: {status}");
         Thread.Sleep(500);
      }
   }
}
