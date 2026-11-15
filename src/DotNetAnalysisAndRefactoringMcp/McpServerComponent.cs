using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Application.Parts;
using JetBrains.Application.Threading;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.Transactions;
using JetBrains.Util;
using Newtonsoft.Json.Linq;
using DotNetAnalysisAndRefactoringMcp.Disk;
using DotNetAnalysisAndRefactoringMcp.Protocol;
using DotNetAnalysisAndRefactoringMcp.Swapping;
using DotNetAnalysisAndRefactoringMcp.Tools;

namespace DotNetAnalysisAndRefactoringMcp;

[SolutionComponent(Instantiation.ContainerAsyncAnyThreadSafe)]
public class McpServerComponent : IDisposable
{
   const int ToolTimeoutSeconds = 120;
   readonly McpShellComponent _shellComponent;
   readonly string _solutionPath;
   readonly SolutionRoot _root;
   readonly ILogger _logger;
   readonly Lifetime _lifetime;
   readonly Lazy<IPsiCachesState> _caches;
   readonly DiskChanges _diskChanges;
   readonly DiskSync _diskSync;
   readonly ISolution _solution;
   readonly IShellLocks _shellLocks;
   readonly SwappableTools _swappableTools;

   public McpServerComponent(
      Lifetime lifetime,
      ISolution solution,
      IShellLocks shellLocks,
      McpShellComponent shellComponent,
      SwappableTools swappableTools,
      ILogger logger)
   {
      _lifetime = lifetime;
      _solution = solution;
      _shellLocks = shellLocks;
      _swappableTools = swappableTools;
      _shellComponent = shellComponent;
      _logger = logger;
      _caches = new Lazy<IPsiCachesState>(() => solution.TryGetComponent<IPsiCachesState>());
      _solutionPath = solution.SolutionFilePath?.FullPath ?? "";

      // Rider 2026.3's lightweight backend loads the plugin too, with a solution that has no file. Registered, it
      // made "Multiple solutions are open" of every call that names no file, and no call can name it.
      if(_solutionPath.Length == 0)
         return;

      _root = SolutionRoot.Of(solution);
      _diskChanges = new DiskChanges();
      _diskSync = new DiskSync(solution, shellLocks, _diskChanges);
      lifetime.OnTermination(_diskChanges);

      // Watching a large tree can take a while on Linux (one inotify watch per directory).
      if(_root.Workspace != null)
         ThreadPool.QueueUserWorkItem(_ => _diskChanges.Watch(_root.Workspace));

      var solutionName = Path.GetFileNameWithoutExtension(_solutionPath);

      _shellComponent.RegisterSolution(solutionName, _solutionPath, ListTools, CallTool, Health);

      lifetime.OnTermination(this);
   }

   bool IsIndexing => _caches.Value is {} caches && !caches.IsIdle.Value;

   JObject Health() => new JObject
                       {
                          ["indexing"] = IsIndexing,
                          ["pendingDiskChanges"] = _diskSync.Pending,
                          ["tools"] = _swappableTools.Status()
                       };

   /// <summary>The current tools build's tools, described in JSON of the host's own: none of the build's objects.</summary>
   List<ToolDefinition> ListTools()
   {
      using var lease = _swappableTools.Lease();
      return lease.CreateTools(_solution)
                  .Select(tool => new ToolDefinition
                                  {
                                     Name = tool.Name,
                                     Description = tool.Description,
                                     InputSchema = JsonOf.Value(tool.InputSchema)
                                  })
                  .ToList();
   }

   /// <summary>The answer of the current tools build's tool <paramref name="name"/>, or null if it has none by that name.</summary>
   string CallTool(string name, JObject args)
   {
      using var lease = _swappableTools.Lease();
      var tool = lease.CreateTools(_solution).FirstOrDefault(it => it.Name == name);

      return tool == null ? null : ExecuteAsync(tool, args, lease).GetAwaiter().GetResult();
   }

   async Task<string> ExecuteAsync(IMcpTool tool, JObject args, ToolsLease lease)
   {
      using var request = Lifetime.Define(_lifetime, $"DotNetAnalysisAndRefactoringMcp.{tool.Name}");
      using var timer = new CancellationTokenSource();
      var execution = new McpToolExecution(request.Lifetime, _shellLocks, _solution);
      var task = RunAsync();
      // A call that times out goes on until it notices its lifetime ended, on the build it started on.
      var running = lease.Extend();
      _ = task.ContinueWith(_ => running.Dispose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
      var timeout = Task.Delay(TimeSpan.FromSeconds(ToolTimeoutSeconds), timer.Token);

      if(await Task.WhenAny(task, timeout).ConfigureAwait(false) != task)
      {
         request.Terminate();
         // As text: an exception kept keeps loaded every build its stack trace runs through.
         _ = task.ContinueWith(completed => _logger.Warn($"DotNetAnalysisAndRefactoringMcp: '{tool.Name}' failed after timing out: {completed.Exception}"),
                               CancellationToken.None,
                               TaskContinuationOptions.OnlyOnFaulted,
                               TaskScheduler.Default);
         throw new TimeoutException(
            $"Timed out after {ToolTimeoutSeconds}s processing '{tool.Name}'. "
          + "The IDE may be busy indexing or performing another operation.");
      }

      timer.Cancel();

      return JsonOf.Text(await task.ConfigureAwait(false));

      async Task<object> RunAsync()
      {
         // Files the caller named, not the ones an expansion added: only those deserve a note of their own.
         var requested = FileArguments.Absolute(args, _root);
         if(tool is IMcpExpandableTool expandable)
            args = expandable.Expand(args);

         var disk = await _diskSync.Run(request.Lifetime, execution, FileArguments.Absolute(args, _root))
                                   .ConfigureAwait(false);

         var refusal = Refusal(disk);
         if(refusal != null)
            return new { error = refusal };

         var result = await (tool switch
                               {
                                  IMcpAsyncTool asyncTool => asyncTool.ExecuteAsync(args, execution),
                                  IMcpWriteTool           => ExecuteWriteAsync(),
                                  _                       => execution.Read(() => tool.Execute(args)),
                               }).ConfigureAwait(false);

         return WithNotes(result, Notes(disk, requested));
      }

      Task<object> ExecuteWriteAsync() =>
         _shellLocks.ExecuteOrQueueReadLockAsync(request.Lifetime,
                                                 $"DotNetAnalysisAndRefactoringMcp.{tool.Name}",
                                                 () =>
                                                 {
                                                    object result = null;
                                                    _shellLocks.ExecuteWithWriteLock(() =>
                                                    {
                                                       if(!request.Lifetime.IsAlive)
                                                          throw new OperationCanceledException();

                                                       _solution.GetPsiServices().Files.CommitAllDocuments();
                                                       if(tool is IMcpSelfTransactingWriteTool)
                                                       {
                                                          result = tool.Execute(args);
                                                       } else
                                                       {
                                                          using var transaction = PsiTransactionCookie.CreateAutoCommitCookieWithCachesUpdate(
                                                             _solution.GetPsiServices(),
                                                             $"DotNetAnalysisAndRefactoringMcp.{tool.Name}");
                                                          result = tool.Execute(args);
                                                       }
                                                    });

                                                    return result;
                                                 });

      // An edit applied to Rider's old copy of a file would be saved over the newer text on disk.
      string Refusal(DiskReport disk)
      {
         if(tool is not IMcpEditTool || args["dryRun"]?.Value<bool>() == true)
            return null;

         if(disk.Stale.Count == 0)
            return null;

         return $"Refused: {tool.Name} edits Rider's copy of files and saves it, and Rider's copy of these "
              + "differs from disk (unsaved edits in Rider, or Rider has not reloaded the file yet):"
              + string.Concat(disk.Stale.Select(x => $"\n  {x}"))
              + "\nSave or revert unsaved edits in Rider, or re-run in a few seconds.";
      }
   }

   List<string> Notes(DiskReport disk, IReadOnlyList<string> requested)
   {
      var notes = new List<string>();
      if(disk.Stale.Count > 0)
         notes.Add($"note: Rider's copy of {Listed(disk.Stale)} differs from disk (unsaved edits in Rider, or not "
                 + "reloaded yet); results reflect Rider's copy");

      var absent = disk.Absent.Where(requested.Contains).ToList();
      if(absent.Count > 0)
         notes.Add($"note: {Listed(absent)} {(absent.Count == 1 ? "is" : "are")} on disk but in no project yet");

      if(IsIndexing)
         notes.Add("(index still building — results may be incomplete)");

      notes.AddRange(_swappableTools.Notes());

      return notes;
   }

   string Listed(IReadOnlyList<string> files)
   {
      const int shown = 3;
      var listed = string.Join(", ", files.Take(shown).Select(_root.Relative));

      return files.Count > shown ? $"{files.Count} files ({listed}, +{files.Count - shown} more)" : listed;
   }

   static object WithNotes(object result, List<string> notes)
   {
      if(notes.Count == 0)
         return result;

      return $"{JsonOf.Text(result)}\n\n{string.Join("\n", notes)}";
   }

   public void Dispose()
   {
      _shellComponent.UnregisterSolution(_solutionPath);
   }
}
