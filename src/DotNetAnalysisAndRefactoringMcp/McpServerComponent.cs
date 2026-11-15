using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using JetBrains.Application.Parts;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using DotNetAnalysisAndRefactoringMcp.Disk;
using DotNetAnalysisAndRefactoringMcp.Swapping;
using DotNetAnalysisAndRefactoringMcp.Threading;
using DotNetAnalysisAndRefactoringMcp.Tools;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   An open solution, registered with the MCP server: it holds what of the solution outlives a tools build, the file
///   watcher and the disk sync's memory, which the tools build that serves a request reaches through <see cref="For"/>.
/// </summary>
[SolutionComponent(Instantiation.ContainerAsyncAnyThreadSafe)]
public class McpServerComponent : IDisposable
{
   readonly McpShellComponent _shellComponent;
   readonly Lifetime _lifetime;
   readonly SolutionRoot _root;
   readonly DiskChanges _diskChanges;
   readonly IThreadShared<DiskSyncState> _diskSyncState = ThreadShared.New(new DiskSyncState());
   readonly ISolution _solution;
   readonly SwappableTools _swappableTools;

   public McpServerComponent(
      Lifetime lifetime,
      ISolution solution,
      McpShellComponent shellComponent,
      SwappableTools swappableTools)
   {
      _lifetime = lifetime;
      _solution = solution;
      _swappableTools = swappableTools;
      _shellComponent = shellComponent;
      FilePath = solution.SolutionFilePath?.FullPath ?? "";

      // Rider 2026.3's lightweight backend loads the plugin too, with a solution that has no file. Registered, it
      // made "Multiple solutions are open" of every call that names no file, and no call can name it.
      if(FilePath.Length == 0)
         return;

      _root = SolutionRoot.Of(solution);
      _diskChanges = new DiskChanges();
      lifetime.OnTermination(_diskChanges);

      // Watching a large tree can take a while on Linux (one inotify watch per directory).
      if(_root.Workspace != null)
         ThreadPool.QueueUserWorkItem(_ => _diskChanges.Watch(_root.Workspace));

      _shellComponent.RegisterSolution(this);

      lifetime.OnTermination(this);
   }

   /// <inheritdoc cref="IMcpSolution.Name"/>
   public string Name => Path.GetFileNameWithoutExtension(FilePath);

   /// <inheritdoc cref="IMcpSolution.FilePath"/>
   public string FilePath { get; }

   /// <summary>The solution as one request on <paramref name="lease"/>'s tools build sees it.</summary>
   public IMcpSolution For(ToolsLease lease) => new LeasedSolution(this, lease);

   public void Dispose()
   {
      _shellComponent.UnregisterSolution(this);
   }

   /// <summary>The solution, as one request on <paramref name="lease"/>'s build sees it.</summary>
   sealed class LeasedSolution(McpServerComponent component, ToolsLease lease) : IMcpSolution
   {
      readonly McpServerComponent _component = component;
      readonly ToolsLease _lease = lease;

      public string Name => _component.Name;

      public string FilePath => _component.FilePath;

      public ISolution Solution => _component._solution;

      public Lifetime Lifetime => _component._lifetime;

      public SolutionRoot Root => _component._root;

      public DiskChanges Changes => _component._diskChanges;

      public IThreadShared<DiskSyncState> DiskSyncState => _component._diskSyncState;

      public IReadOnlyList<string> BuildNotes => _component._swappableTools.Notes();

      public IDisposable KeepTheBuildLoaded() => _lease.Extend();
   }
}
