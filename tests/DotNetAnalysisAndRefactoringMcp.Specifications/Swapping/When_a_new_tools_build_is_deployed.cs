using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Swapping;

/// <summary>
///   A client with an event stream open, as Claude Code keeps one, while a new tools build is deployed and no call
///   comes: until 2026-11-14 a tool the new build added appeared only when the client reconnected.
/// </summary>
public class When_a_new_tools_build_is_deployed
{
   static readonly TimeSpan HowLongNoticingMayTake = TimeSpan.FromSeconds(20);

   readonly bool _told;

   public When_a_new_tools_build_is_deployed(TheSandboxWithTheFixtureSolution sandbox)
   {
      using var events = sandbox.McpServer.OpenEventStream();
      sandbox.DeployTheToolsAgain();
      _told = events.Sends("notifications/tools/list_changed", HowLongNoticingMayTake);
   }

   [XF] public void it_tells_the_client_that_the_tool_list_changed() => _told.Must().Be(true);
}
