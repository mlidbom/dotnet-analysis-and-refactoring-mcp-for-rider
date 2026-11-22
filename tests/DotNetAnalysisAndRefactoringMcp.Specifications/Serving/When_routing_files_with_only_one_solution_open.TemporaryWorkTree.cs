using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Processes;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public partial class When_routing_files_with_only_one_solution_open
{
   /// <summary>A detached copy of the fixture's Git work tree, removed once the calls against it have finished.</summary>
   sealed class TemporaryWorkTree : IDisposable
   {
      readonly string _openWorkTree;
      public string Folder { get; }

      public TemporaryWorkTree(string openWorkTree, string parent)
      {
         _openWorkTree = openWorkTree;
         Folder = Path.GetFullPath(Path.Combine(openWorkTree, parent, $"Routing-{Guid.NewGuid():N}"));
         ExternalCommand.Run("git", ["worktree", "add", "--quiet", "--detach", Folder, "HEAD"], _openWorkTree);
      }

      public void Dispose() => ExternalCommand.Run("git", ["worktree", "remove", Folder], _openWorkTree);
   }
}
