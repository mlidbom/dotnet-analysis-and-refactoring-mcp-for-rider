#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DotNetAnalysisAndRefactoringMcp.Threading;
using JetBrains.Application;
using JetBrains.Application.Threading;
using JetBrains.DocumentManagers.impl;
using JetBrains.DocumentModel;
using JetBrains.DocumentModel.Storage;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   Brings Rider's copy of files in line with disk before a tool reads them.
/// </summary>
/// <remarks>
///   <para>
///     Suspects are the files a call names, the files <see cref="DiskChanges"/> saw change, and the files that
///     differed before. For a suspect whose document differs from disk, or a named file on disk in no project, the
///     frontend is asked to rescan it (and the folder of a new file), and the call waits until the backend has the
///     new text. A file still different after that wait is not waited on again until its disk time changes.
///   </para>
///   <para>
///     The frontend's rescan alone lands anywhere from 0.1 s to 9 s later (measured). So stale documents are
///     also reconciled on the backend, through Rider's own <c>SynchronizeDocumentContentsWithStorage</c>: for a
///     document no editor owns, it applies disk text as an external change and marks the file reconciled; for one
///     an editor owns, Rider leaves the text to the frontend. Never edit a document's text directly instead: that
///     is an in-memory edit, the document counts as unsaved, and autosave later writes it over whatever is on disk
///     by then (observed with <c>DocumentBindingUtil.SyncDocumentsWithFiles</c>). A document with unsaved edits is
///     never reconciled. A file that still differs is reported as such, and edit tools refuse it.
///   </para>
/// </remarks>
public sealed class DiskSync
{
   static readonly ILogger Log = Logger.GetLogger<DiskSync>();

   // A tab open in an editor reloads at the frontend's pace: 0.2 s to 9 s measured. Waiting beats a stale answer.
   static readonly TimeSpan ReloadTimeout = TimeSpan.FromSeconds(10);
   static readonly TimeSpan ReloadPoll = TimeSpan.FromMilliseconds(100);

   // Files compared with disk per call; past it (a branch switch), the rest are only rescanned.
   const int MaxChecked = 500;

   readonly IThreadShared<DiskSyncState> _settled;
   readonly ISolution _solution;
   readonly IShellLocks _locks;
   readonly DiskChanges _changes;

   /// <param name="settled">What the disk sync remembers from one call to the next, which the host holds.</param>
   public DiskSync(ISolution solution, IShellLocks locks, DiskChanges changes, IThreadShared<DiskSyncState> settled)
   {
      _solution = solution;
      _locks = locks;
      _changes = changes;
      _settled = settled;
   }

   /// <param name="named">Absolute paths of the files the call names.</param>
   public async Task<DiskReport> Run(Lifetime lifetime, McpToolExecution execution, IReadOnlyCollection<string> named)
   {
      await WatchProjects(execution);

      var changes = _changes.Take();
      var suspects = new HashSet<string>(named.Concat(Known()).Concat(changes.Files.Take(MaxChecked)), StringComparer.Ordinal);
      var unverified = changes.Files.Where(x => !suspects.Contains(x)).ToList();
      var before = await execution.Read(() => Check(suspects));

      var stale = before.Stale.Where(x => IsUnsettled(it => it.Stale, x)).ToList();
      if(stale.Count > 0)
         await Reconcile(lifetime, stale);

      var absent = before.Absent.Where(x => IsUnsettled(it => it.Absent, x)).ToList();
      var rescan = stale
                  .Concat(absent)
                  .Concat(absent.Select(Path.GetDirectoryName))
                  .Concat(changes.Directories)
                  .Concat(unverified)
                  .Distinct()
                  .ToList();

      if(rescan.Count > 0)
         await new Rescans(_solution, _locks).Request(lifetime, rescan);

      var waited = stale.Concat(absent.Where(named.Contains)).ToList();
      var after = waited.Count > 0 ? await WaitForReload(execution, waited) : DiskState.Clean;
      var state = before.Without(waited).With(after);
      Remember(suspects, state);

      return new DiskReport(state.Stale, state.Absent.Where(named.Contains).ToList());
   }

   IReadOnlyList<string> Known() => _settled.Locked(it => it.Stale.Keys.ToList());

   async Task WatchProjects(McpToolExecution execution)
   {
      if(_settled.Locked(it => it.WatchesProjects))
         return;

      _settled.Locked(it => it.WatchesProjects = true);
      var directories = await execution.Read(() => _solution.GetAllProjects()
                                                            .Select(x => x.Location)
                                                            .Where(x => !x.IsEmpty)
                                                            .Select(x => x.FullPath)
                                                            .Select(x => Directory.Exists(x) ? x : Path.GetDirectoryName(x))
                                                            .Where(x => x != null)
                                                            .Distinct()
                                                            .ToList());

      foreach(var directory in directories)
         _changes.Watch(directory!);
   }

   /// <summary>
   ///   Reconciles stale documents with disk on the main thread under the write lock. Whether a document has unsaved
   ///   edits is asked there, so an edit made since the check is never overwritten.
   /// </summary>
   async Task Reconcile(Lifetime lifetime, IReadOnlyCollection<string> files)
   {
      try
      {
         await _locks.ExecuteOrQueueReadLockAsync(lifetime,
                                                  "DotNetAnalysisAndRefactoringMcp.DiskSync.Reconcile",
                                                  () =>
                                                  {
                                                     _locks.ExecuteWithWriteLock(() =>
                                                     {
                                                        if(_solution.TryGetComponent<IDocumentStorageHelpers>() is not DocumentOperationsImpl operations)
                                                           return;

                                                        foreach(var file in files)
                                                        {
                                                           var document = Document(file);
                                                           if(document != null && !operations.IsDocumentUnsaved(document))
                                                              operations.SynchronizeDocumentContentsWithStorage(document, Parse(file));
                                                        }

                                                        _solution.GetPsiServices().Files.CommitAllDocuments();
                                                     });
                                                  });
      }
      catch(Exception exception) when(exception is not OperationCanceledException)
      {
         Log.Warn(exception, ExceptionOrigin.Algorithmic, "DotNetAnalysisAndRefactoringMcp could not reconcile changed files with disk");
      }
   }

   async Task<DiskState> WaitForReload(McpToolExecution execution, IReadOnlyCollection<string> files)
   {
      var deadline = DateTime.UtcNow + ReloadTimeout;
      while(true)
      {
         var state = await execution.Read(() => Check(files));
         if(state.IsClean || DateTime.UtcNow > deadline)
            return state;

         await Task.Delay(ReloadPoll);
      }
   }

   DiskState Check(IEnumerable<string> files)
   {
      var stale = new List<string>();
      var absent = new List<string>();
      foreach(var file in files)
      {
         Interruption.Current.CheckAndThrow();
         if(!File.Exists(file))
            continue;

         var document = Document(file);
         if(document == null)
            absent.Add(file);
         else if(!DiskText.Holds(file, document.GetText()))
            stale.Add(file);
      }

      return new DiskState(stale, absent);
   }

   IDocument? Document(string file) =>
      _solution.FindProjectItemsByLocation(Parse(file))
               .OfType<IProjectFile>()
               .Select(x => x.ToSourceFiles().FirstOrDefault())
               .FirstOrDefault(x => x != null)
              ?.Document;

   /// <summary>
   ///   Whether a file is worth a reload and a wait: it was not left in <paramref name="settled"/> by an earlier
   ///   call, or it changed on disk since.
   /// </summary>
   bool IsUnsettled(Func<DiskSyncState, Dictionary<string, DateTime>> settled, string file) =>
      _settled.Locked(it => !settled(it).TryGetValue(file, out var time) || time != DiskTime(file));

   void Remember(IEnumerable<string> checkedFiles, DiskState state)
   {
      _settled.Locked(it =>
      {
         foreach(var file in checkedFiles)
         {
            it.Stale.Remove(file);
            it.Absent.Remove(file);
         }

         foreach(var file in state.Stale)
            it.Stale[file] = DiskTime(file);

         foreach(var file in state.Absent)
            it.Absent[file] = DiskTime(file);
      });
   }

   static DateTime DiskTime(string file) =>
      File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;

   static VirtualFileSystemPath Parse(string path) =>
      VirtualFileSystemPath.Parse(path, InteractionContext.SolutionContext);
}

/// <summary>
///   What a sync could not fix: files whose Rider copy still differs from disk (unsaved edits in Rider, or a reload
///   Rider has not finished), and named files that are on disk but in no project yet.
/// </summary>
public sealed class DiskReport
{
   public DiskReport(IReadOnlyList<string> stale, IReadOnlyList<string> absent) =>
      (Stale, Absent) = (stale, absent);

   public IReadOnlyList<string> Stale { get; }

   public IReadOnlyList<string> Absent { get; }
}

internal sealed class DiskState
{
   public static readonly DiskState Clean = new DiskState(new List<string>(), new List<string>());

   public DiskState(IReadOnlyList<string> stale, IReadOnlyList<string> absent) =>
      (Stale, Absent) = (stale, absent);

   public IReadOnlyList<string> Stale { get; }

   public IReadOnlyList<string> Absent { get; }

   public bool IsClean => Stale.Count == 0 && Absent.Count == 0;

   public DiskState Without(IReadOnlyCollection<string> files) =>
      new DiskState(Stale.Except(files).ToList(), Absent.Except(files).ToList());

   public DiskState With(DiskState other) =>
      new DiskState(Stale.Concat(other.Stale).ToList(), Absent.Concat(other.Absent).ToList());
}
