using DotNetAnalysisAndRefactoringMcp.Serving.Routing;
using Royder.Primary.Transport;

namespace Royder.Primary.Worktrees;

/// <summary>The endpoint that can answer for one exact solution path, whether in an ordinary Rider or the managed JVM.</summary>
sealed class ServingSolution(string name, string path, int toolCount, RiderEndpoint endpoint, bool managed) : SolutionLocation(name, path)
{
   public RiderEndpoint Endpoint { get; } = endpoint;
   public bool Managed { get; } = managed;
   public override int ToolCount { get; } = toolCount;
   public override string WorkTree { get; } = RequestedPath.WorkTreeOf(Path.GetDirectoryName(path)) ?? Path.GetDirectoryName(path)!;

   public override string Absolute(string path)
   {
      var inSolution = Path.GetFullPath(path, Folder);
      if(File.Exists(inSolution) || Directory.Exists(inSolution)) return inSolution;
      var inWorktree = Path.GetFullPath(path, WorkTree);
      return File.Exists(inWorktree) || Directory.Exists(inWorktree) ? inWorktree : inSolution;
   }
}
