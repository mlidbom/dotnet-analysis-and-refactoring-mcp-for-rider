using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotNetAnalysisAndRefactoringMcp.Disk;
using JetBrains.Application.Threading;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Transactions;
using JetBrains.Util;
using JetBrains.Util.Logging;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   One call of one tool, from the arguments to the answer: the disk sync before it, an edit tool refused on a stale
///   file, the dispatch by the tool's kind (a read, a write under the write lock, or the tool's own scheduling), the
///   timeout, and the notes a caller should know of.
/// </summary>
/// <remarks>
///   In the tools, so that changing it is a deploy, not a Rider restart; until 2026-11-15 it was the host's
///   <c>McpServerComponent</c>. What it needs to outlive the build, such as the disk sync's memory, the host holds in
///   <paramref name="solution"/>.
/// </remarks>
sealed class ToolCall(IMcpSolution solution, IMcpTool tool)
{
   /// <summary>How long a call may take before it is answered with a timeout, and its work is asked to stop.</summary>
   public const int TimeoutSeconds = 120;

   static readonly ILogger Log = Logger.GetLogger<ToolCall>();

   readonly IShellLocks _locks = solution.Solution.GetComponent<IShellLocks>();
   readonly IMcpSolution _solution = solution;
   readonly IMcpTool _tool = tool;

   /// <summary>The tool's answer to <paramref name="arguments"/>, as text.</summary>
   public string Answer(JObject arguments) => AnswerAsync(arguments).GetAwaiter().GetResult();

   async Task<string> AnswerAsync(JObject arguments)
   {
      using var request = Lifetime.Define(_solution.Lifetime, $"DotNetAnalysisAndRefactoringMcp.{_tool.Name}");
      using var timer = new CancellationTokenSource();
      var execution = new McpToolExecution(request.Lifetime, _locks, _solution.Solution);
      var task = RunAsync(arguments, request.Lifetime, execution);
      // A call that times out goes on until it notices its lifetime ended, on the build it started on.
      var running = _solution.KeepTheBuildLoaded();
      _ = task.ContinueWith(_ => running.Dispose(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
      var timeout = Task.Delay(TimeSpan.FromSeconds(TimeoutSeconds), timer.Token);

      if(await Task.WhenAny(task, timeout).ConfigureAwait(false) != task)
      {
         request.Terminate();
         // As text: an exception kept keeps loaded every build its stack trace runs through.
         _ = task.ContinueWith(completed => Log.Warn($"DotNetAnalysisAndRefactoringMcp: '{_tool.Name}' failed after timing out: {completed.Exception}"),
                               CancellationToken.None,
                               TaskContinuationOptions.OnlyOnFaulted,
                               TaskScheduler.Default);
         throw new TimeoutException($"Timed out after {TimeoutSeconds}s processing '{_tool.Name}'. "
                                  + "The IDE may be busy indexing or performing another operation.");
      }

      timer.Cancel();
      return JsonOf.Text(await task.ConfigureAwait(false));
   }

   async Task<object> RunAsync(JObject arguments, Lifetime lifetime, McpToolExecution execution)
   {
      // Files the caller named, not the ones an expansion added: only those deserve a note of their own.
      var requested = FileArguments.Absolute(arguments, _solution.Root);
      if(_tool is IMcpExpandableTool expandable)
         arguments = expandable.Expand(arguments);

      var disk = await new DiskSync(_solution.Solution, _locks, _solution.Changes, _solution.DiskSyncState)
                      .Run(lifetime, execution, FileArguments.Absolute(arguments, _solution.Root))
                      .ConfigureAwait(false);

      var refusal = Refusal(disk, arguments);
      if(refusal != null)
         return new { error = refusal };

      var result = await (_tool switch
                            {
                               IMcpAsyncTool asyncTool => asyncTool.ExecuteAsync(arguments, execution),
                               IMcpWriteTool           => WriteAsync(arguments, lifetime),
                               _                       => execution.Read(() => _tool.Execute(arguments)),
                            }).ConfigureAwait(false);

      return WithNotes(result, Notes(disk, requested));
   }

   Task<object> WriteAsync(JObject arguments, Lifetime lifetime) =>
      _locks.ExecuteOrQueueReadLockAsync(lifetime,
                                         $"DotNetAnalysisAndRefactoringMcp.{_tool.Name}",
                                         () =>
                                         {
                                            object result = null;
                                            _locks.ExecuteWithWriteLock(() =>
                                            {
                                               if(!lifetime.IsAlive)
                                                  throw new OperationCanceledException();

                                               var psiServices = _solution.Solution.GetPsiServices();
                                               psiServices.Files.CommitAllDocuments();
                                               if(_tool is IMcpSelfTransactingWriteTool)
                                               {
                                                  result = _tool.Execute(arguments);
                                               } else
                                               {
                                                  using var transaction = PsiTransactionCookie.CreateAutoCommitCookieWithCachesUpdate(
                                                     psiServices,
                                                     $"DotNetAnalysisAndRefactoringMcp.{_tool.Name}");
                                                  result = _tool.Execute(arguments);
                                               }
                                            });

                                            return result;
                                         });

   /// <summary>Why an edit tool may not run: an edit applied to Rider's old copy of a file would be saved over the newer text on disk.</summary>
   string Refusal(DiskReport disk, JObject arguments)
   {
      if(_tool is not IMcpEditTool || arguments["dryRun"]?.Value<bool>() == true)
         return null;

      if(disk.Stale.Count == 0)
         return null;

      return $"Refused: {_tool.Name} edits Rider's copy of files and saves it, and Rider's copy of these "
           + "differs from disk (unsaved edits in Rider, or Rider has not reloaded the file yet):"
           + string.Concat(disk.Stale.Select(x => $"\n  {x}"))
           + "\nSave or revert unsaved edits in Rider, or re-run in a few seconds.";
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

      if(_solution.IsIndexing)
         notes.Add("(index still building — results may be incomplete)");

      notes.AddRange(_solution.BuildNotes);
      return notes;
   }

   string Listed(IReadOnlyList<string> files)
   {
      const int shown = 3;
      var listed = string.Join(", ", files.Take(shown).Select(_solution.Root.Relative));

      return files.Count > shown ? $"{files.Count} files ({listed}, +{files.Count - shown} more)" : listed;
   }

   static object WithNotes(object result, List<string> notes) =>
      notes.Count == 0 ? result : $"{JsonOf.Text(result)}\n\n{string.Join("\n", notes)}";
}
