using System.Diagnostics;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving.WithAPeer;

/// <summary>
///   The second sandbox Rider, the first one's peer, with a fresh copy of the fixture solution of its own open: one with
///   a type the first one's copy doesn't have, <c>OnlyInThePeer</c>. Its MCP server registers the solution with the first
///   sandbox's, which lists it, and forwards the calls for it.
/// </summary>
/// <remarks>
///   Started for the specifications in the collection <see cref="WithThePeerRider"/>, and stopped after them: while it
///   runs, two solutions are open, and the first sandbox refuses a call that names neither, as most specifications'
///   calls are. Stopping it checks that the first sandbox's server forgot the solution, so that the specifications after
///   it find one solution open again.
/// </remarks>
public sealed class ThePeerRider : IDisposable
{
   static readonly TimeSpan HowLongForgettingMayTake = TimeSpan.FromMinutes(1);
   static readonly TimeSpan HowLongResolvingMayTake = TimeSpan.FromMinutes(2);

   readonly TheSandboxWithTheFixtureSolution _sandbox;
   readonly SandboxRider _peer;
   readonly string _solutionFolder;

   public ThePeerRider(TheSandboxWithTheFixtureSolution sandbox)
   {
      _sandbox = sandbox;
      _peer = SandboxRider.ThePeer(RiderInstallation.OfThisUser());
      _peer.Stop();
      _peer.InstallThePluginOf(sandbox.Build);

      _solutionFolder = Path.Combine(_peer.SolutionsFolder, TheSandboxWithTheFixtureSolution.SolutionName);
      TheSandboxWithTheFixtureSolution.CopyTheFixtureSolutionTo(_solutionFolder);
      File.WriteAllText(PathOf("Shapes/OnlyInThePeer.cs"),
                        "namespace Shapes;\n\n/// <summary>A type only the peer's copy of the fixture solution has.</summary>\npublic sealed class OnlyInThePeer;\n");

      try
      {
         _peer.Start(SolutionPath);
         WaitUntilTheSolutionResolves();
      }
      catch
      {
         // A fixture whose constructor throws is never disposed, and a peer left running had every specification after
         // it find two solutions open (2026-11-15).
         _peer.Stop();
         throw;
      }
   }

   /// <summary>The peer's copy of the fixture solution, which the first sandbox's server lists by this path.</summary>
   public string SolutionPath => PathOf(TheSandboxWithTheFixtureSolution.SolutionFile);

   /// <summary>The full path of a file of the peer's copy of the fixture solution, given its path within the solution.</summary>
   public string PathOf(string pathInTheSolution) => Path.GetFullPath(Path.Combine(_solutionFolder, pathInTheSolution));

   public void Dispose()
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
      _sandbox.McpServer.Health().GetProperty("solutions").EnumerateArray()
              .Any(it => string.Equals(it.GetProperty("path").GetString(), SolutionPath, StringComparison.OrdinalIgnoreCase));

   /// <summary>Through the first sandbox, as every call to the peer goes: as <c>TheSandboxWithTheFixtureSolution</c> waits for its own copy.</summary>
   void WaitUntilTheSolutionResolves()
   {
      var waiting = Stopwatch.StartNew();
      string answer;
      while(!(answer = _sandbox.McpServer.CallTool("get_file_errors", new { filePath = PathOf("Shapes.Drawing/Drawing.cs") })).Contains("0 diagnostics"))
      {
         if(waiting.Elapsed > HowLongResolvingMayTake)
            throw new TimeoutException($"The peer's copy of the fixture solution still doesn't resolve after {HowLongResolvingMayTake}. get_file_errors answers: {answer}");
         Thread.Sleep(500);
      }
   }
}
