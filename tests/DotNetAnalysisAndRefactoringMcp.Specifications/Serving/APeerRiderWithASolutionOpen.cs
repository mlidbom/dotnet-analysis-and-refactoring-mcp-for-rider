using System.Diagnostics;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

/// <summary>
///   The second sandbox Rider, the first one's peer, with a solution open: its MCP server registers the solution with
///   the first sandbox's, which lists it, and forwards the calls for it.
/// </summary>
/// <remarks>
///   Started for the specifications of a collection, and stopped after them: while it runs, two solutions are open, and
///   the first sandbox refuses a call that names neither, as most specifications' calls are. Stopping it checks that the
///   first sandbox's server forgot the solution, so that the specifications after it find one solution open again.
/// </remarks>
public abstract class APeerRiderWithASolutionOpen : IDisposable
{
   static readonly TimeSpan HowLongForgettingMayTake = TimeSpan.FromMinutes(1);
   static readonly TimeSpan HowLongResolvingMayTake = TimeSpan.FromMinutes(2);

   readonly SandboxRider _peer;

   protected APeerRiderWithASolutionOpen(TheSandboxWithTheFixtureSolution sandbox)
   {
      Sandbox = sandbox;
      _peer = SandboxRider.ThePeer(RiderInstallation.OfThisUser());
      _peer.Stop();
      _peer.InstallThePluginOf(sandbox.Build);
   }

   protected TheSandboxWithTheFixtureSolution Sandbox { get; }

   /// <summary>Where solutions to open in the peer go, which it trusts.</summary>
   protected string PeersSolutionsFolder => _peer.SolutionsFolder;

   /// <summary>The solution the peer has open, which the first sandbox's server lists by this path.</summary>
   public abstract string SolutionPath { get; }

   /// <summary>
   ///   Opens <see cref="SolutionPath"/> in the peer, and returns once a call through the first sandbox for
   ///   <paramref name="aFileOfIt"/> finds it resolved.
   /// </summary>
   protected void Open(string aFileOfIt)
   {
      try
      {
         _peer.Start(SolutionPath);
         WaitUntilItResolves(aFileOfIt);
      }
      catch
      {
         // A fixture whose constructor throws is never disposed, and a peer left running had every specification after
         // it find two solutions open (2026-11-15).
         _peer.Stop();
         throw;
      }
   }

   public virtual void Dispose()
   {
      _peer.Stop();
      var waiting = Stopwatch.StartNew();
      while(TheFirstSandboxListsTheSolution())
      {
         if(waiting.Elapsed > HowLongForgettingMayTake)
            throw new InvalidOperationException($"The first sandbox's MCP server still lists {SolutionPath} {HowLongForgettingMayTake} after the peer closed it: "
                                              + "the peer's deregistering failed, and the specifications after these find two solutions open.");
         Thread.Sleep(500);
      }
   }

   bool TheFirstSandboxListsTheSolution() =>
      Sandbox.McpServer.Health().GetProperty("solutions").EnumerateArray()
             .Any(it => string.Equals(it.GetProperty("path").GetString(), SolutionPath, StringComparison.OrdinalIgnoreCase));

   /// <summary>Through the first sandbox, as every call to the peer goes: as <c>TheSandboxWithTheFixtureSolution</c> waits for its own copy.</summary>
   void WaitUntilItResolves(string file)
   {
      var waiting = Stopwatch.StartNew();
      string answer;
      while(!(answer = Sandbox.McpServer.CallTool("get_file_errors", new { filePath = file })).Contains("0 diagnostics"))
      {
         if(waiting.Elapsed > HowLongResolvingMayTake)
            throw new TimeoutException($"The peer's {SolutionPath} still doesn't resolve after {HowLongResolvingMayTake}. get_file_errors answers for {file}: {answer}");
         Thread.Sleep(500);
      }
   }
}
