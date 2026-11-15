using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using JetBrains.Application.Parts;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi.Caches;
using Newtonsoft.Json.Linq;
using DotNetAnalysisAndRefactoringMcp.Disk;
using DotNetAnalysisAndRefactoringMcp.Protocol;
using DotNetAnalysisAndRefactoringMcp.Swapping;
using DotNetAnalysisAndRefactoringMcp.Threading;
using DotNetAnalysisAndRefactoringMcp.Tools;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   An open solution, registered with the MCP server: it holds what of the solution outlives a tools build, the file
///   watcher and the disk sync's memory, and hands each call to the current tools build.
/// </summary>
[SolutionComponent(Instantiation.ContainerAsyncAnyThreadSafe)]
public class McpServerComponent : IDisposable
{
   readonly McpShellComponent _shellComponent;
   readonly string _solutionPath;
   readonly SolutionRoot _root;
   readonly Lifetime _lifetime;
   readonly Lazy<IPsiCachesState> _caches;
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
      _caches = new Lazy<IPsiCachesState>(() => solution.TryGetComponent<IPsiCachesState>());
      _solutionPath = solution.SolutionFilePath?.FullPath ?? "";

      // Rider 2026.3's lightweight backend loads the plugin too, with a solution that has no file. Registered, it
      // made "Multiple solutions are open" of every call that names no file, and no call can name it.
      if(_solutionPath.Length == 0)
         return;

      _root = SolutionRoot.Of(solution);
      _diskChanges = new DiskChanges();
      lifetime.OnTermination(_diskChanges);

      // Watching a large tree can take a while on Linux (one inotify watch per directory).
      if(_root.Workspace != null)
         ThreadPool.QueueUserWorkItem(_ => _diskChanges.Watch(_root.Workspace));

      _shellComponent.RegisterSolution(Path.GetFileNameWithoutExtension(_solutionPath), _solutionPath, ListTools, CallTool, Health);

      lifetime.OnTermination(this);
   }

   bool IsIndexing => _caches.Value is {} caches && !caches.IsIdle.Value;

   JObject Health() => new JObject
                       {
                          ["indexing"] = IsIndexing,
                          ["pendingDiskChanges"] = _diskChanges.Count + _diskSyncState.Locked(it => it.Stale.Count),
                          ["tools"] = _swappableTools.Status()
                       };

   List<ToolDefinition> ListTools()
   {
      using var lease = _swappableTools.Lease();
      return lease.ListTools(_solution);
   }

   /// <summary>The answer of the current tools build's tool <paramref name="name"/>, or null if it has none by that name.</summary>
   string CallTool(string name, JObject arguments)
   {
      using var lease = _swappableTools.Lease();
      return lease.CallTool(new Call(this, lease), name, arguments);
   }

   public void Dispose()
   {
      _shellComponent.UnregisterSolution(_solutionPath);
   }

   /// <summary>The solution, as one call on <paramref name="lease"/>'s build sees it.</summary>
   sealed class Call(McpServerComponent component, ToolsLease lease) : IMcpSolution
   {
      public ISolution Solution => component._solution;

      public Lifetime Lifetime => component._lifetime;

      public SolutionRoot Root => component._root;

      public DiskChanges Changes => component._diskChanges;

      public IThreadShared<DiskSyncState> DiskSyncState => component._diskSyncState;

      public bool IsIndexing => component.IsIndexing;

      public IReadOnlyList<string> BuildNotes => component._swappableTools.Notes();

      public IDisposable KeepTheBuildLoaded() => lease.Extend();
   }
}
