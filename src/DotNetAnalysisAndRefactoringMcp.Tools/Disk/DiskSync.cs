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
using JetBrains.ProjectModel.ProjectsHost;
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
///     differed before. The frontend rescans changed files and the folders of added or deleted files. The call waits
///     for current document text, for a new file the call names or a project should include, and for a deleted file
///     to leave the project model. Rider keeps some explicitly included files as missing project items. A call that
///     waited for such a deletion reports it. Later calls can edit other files while the item remains missing.
///   </para>
///   <para>
///     The frontend's rescan alone lands anywhere from 0.1 s to 9 s later (measured). So stale documents are
///     also reconciled on the backend (<see cref="Reconciliation"/>). A file that still differs is reported as such,
///     and edit tools refuse it.
///   </para>
/// </remarks>
public sealed class DiskSync
{
   // A tab open in an editor reloads at the frontend's pace: 0.2 s to 9 s measured. Waiting beats a stale answer.
   static readonly TimeSpan ReloadTimeout = TimeSpan.FromSeconds(10);
   static readonly TimeSpan ReloadPoll = TimeSpan.FromMilliseconds(100);

   /// <summary>
   ///   How long the watcher's events of a change on disk may take to arrive. A rename sent as a file was written found
   ///   the backend's copy still old, since the watcher hadn't reported the file yet (2026-11-24).
   /// </summary>
   static readonly TimeSpan WatcherLatency = TimeSpan.FromMilliseconds(100);

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
   /// <param name="beforeAnEdit">
   ///   Whether an edit follows. Rider's frontend applies an edit to its own copy of each file, at the offsets the backend
   ///   computed on its copy: the two must hold the same text (<see cref="FileNotTheEditsResult"/>). So the watcher's
   ///   events get <see cref="WatcherLatency"/> to arrive, and the frontend is asked to rescan every suspect, not only
   ///   those the backend had stale: the frontend can lag where the backend doesn't, as it did while the private Rider
   ///   was opening a solution.
   /// </param>
   public async Task<DiskReport> Run(Lifetime lifetime, McpToolExecution execution, IReadOnlyCollection<string> named, bool beforeAnEdit)
   {
      await WatchProjects(execution);
      if(beforeAnEdit)
         await Task.Delay(WatcherLatency, lifetime);

      var (changes, suspects) = TakeSuspects(named);
      var unverified = changes.Files.Where(x => !suspects.Contains(x)).ToList();
      var before = await execution.Read(() => Check(suspects));

      var stale = before.Stale.Where(x => IsUnsettled(it => it.Stale, x)).ToList();
      if(stale.Count > 0)
         await new Reconciliation(_solution, _locks).Reconcile(lifetime, () => stale.Select(Document).OfType<IDocument>());

      var deleted = before.Deleted.Where(x => IsUnsettled(it => it.Absent, x)).ToList();
      var absent = before.Absent.Where(x => IsUnsettled(it => it.Absent, x)).ToList();
      var rescan = stale.Concat(deleted).Select(PathToRescan)
                  .Concat(absent)
                  .Concat(absent.Select(Path.GetDirectoryName))
                  .Concat(changes.Directories)
                  .Concat(unverified.Select(PathToRescan))
                  .Concat(beforeAnEdit ? suspects.Where(File.Exists) : [])
                  .Distinct()
                  .ToList();

      if(rescan.Count > 0)
         await new Rescans(_solution, _locks).Request(lifetime, rescan);

      IReadOnlyList<string> projectFolders = absent.Count == 0 ? [] : await execution.Read(ProjectFolders);
      var waited = stale.Concat(deleted).Concat(absent.Where(WillJoinAProject)).ToList();
      var after = waited.Count > 0 ? await WaitForReload(execution, waited) : DiskState.Clean;
      var state = before.Without(waited).With(after);
      Remember(suspects, state);

      return new DiskReport(state.Stale, state.Absent.Where(named.Contains).ToList(), after.Deleted);

      // A new file worth a wait: one the call names, or one in a project's folder, which an SDK-style project takes
      // in. A call that reaches its types by name, such as a search, finds them only once Rider has it. Not every new
      // file the work tree watch sees: one in no project's folder would cost each call after it the whole wait.
      bool WillJoinAProject(string file) =>
         named.Contains(file)
      || projectFolders.Any(folder => file.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
   }

   /// <summary>Rescanning the containing folder lets Rider discover that a file has disappeared.</summary>
   static string PathToRescan(string file) => File.Exists(file) ? file : Path.GetDirectoryName(file)!;

   /// <summary>The folders of the solution's project files.</summary>
   IReadOnlyList<string> ProjectFolders() =>
      _solution.GetAllProjects()
               .Where(it => !it.ProjectFileLocation.IsEmpty && it.GetProjectMark() != null)
               .Select(it => it.ProjectFileLocation.Directory.FullPath)
               .Distinct()
               .ToList();

   /// <summary>
   ///   Taking a watcher event consumes it. Share the paths to check in the same lock so that a concurrent edit
   ///   also checks a deletion while the call that took the event waits for Rider.
   /// </summary>
   (DiskChangeSet Changes, HashSet<string> Suspects) TakeSuspects(IReadOnlyCollection<string> named) =>
      _settled.Locked(state =>
      {
         var changes = _changes.Take();
         foreach(var file in named.Concat(changes.Files.Take(MaxChecked)))
            if(!state.Stale.ContainsKey(file))
               //refactor: DiskSyncState.Stale holds two things since 4a3cd7f: files still stale after a wait, with
               //   their disk times, and paths awaiting their first check, as DateTime.MinValue, so that a concurrent
               //   call checks them too. DiskSyncState should get a set of its own for the paths awaiting a check,
               //   with the next host change: a change to the host waits for a restart of every Rider.
               state.Stale.Add(file, DateTime.MinValue);

         return (changes, new HashSet<string>(state.Stale.Keys, StringComparer.Ordinal));
      });

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
      var deleted = new List<string>();
      foreach(var file in files)
      {
         Interruption.Current.CheckAndThrow();
         if(!File.Exists(file))
         {
            if(ProjectFilesAt(file).Any())
               deleted.Add(file);
            continue;
         }

         var document = Document(file);
         if(document == null)
            absent.Add(file);
         else if(!DiskText.Holds(file, document.GetText()))
            stale.Add(file);
      }

      return new DiskState(stale, absent, deleted);
   }

   IEnumerable<IProjectFile> ProjectFilesAt(string file) => _solution.FindProjectItemsByLocation(Parse(file)).OfType<IProjectFile>();

   IDocument? Document(string file) =>
      ProjectFilesAt(file).Select(it => it.ToSourceFiles().FirstOrDefault())
                          .FirstOrDefault(it => it != null)
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

         //refactor: DiskSyncState.Absent is for files on disk in no project. A file deleted on disk and still in its
         //   project is the opposite, kept there, with no disk time, only so that it is settled without a host
         //   change. DiskSyncState should get a dictionary of its own for it, with the next host change.
         foreach(var file in state.Deleted)
            it.Absent[file] = DateTime.MinValue;
      });
   }

   static DateTime DiskTime(string file) =>
      File.Exists(file) ? File.GetLastWriteTimeUtc(file) : DateTime.MinValue;

   static VirtualFileSystemPath Parse(string path) =>
      VirtualFileSystemPath.Parse(path, InteractionContext.SolutionContext);
}

/// <summary>
///   The differences a tool call reports after disk sync: stale documents, named files missing from the project,
///   and files this call waited for Rider to remove from the project after deletion.
/// </summary>
public sealed class DiskReport
{
   public DiskReport(IReadOnlyList<string> stale, IReadOnlyList<string> absent, IReadOnlyList<string> deleted) =>
      (Stale, Absent, Deleted) = (stale, absent, deleted);

   public IReadOnlyList<string> Stale { get; }

   public IReadOnlyList<string> Absent { get; }

   public IReadOnlyList<string> Deleted { get; }
}

internal sealed class DiskState
{
   public static readonly DiskState Clean = new DiskState([], [], []);

   public DiskState(IReadOnlyList<string> stale, IReadOnlyList<string> absent, IReadOnlyList<string> deleted) =>
      (Stale, Absent, Deleted) = (stale, absent, deleted);

   public IReadOnlyList<string> Stale { get; }

   public IReadOnlyList<string> Absent { get; }

   public IReadOnlyList<string> Deleted { get; }

   public bool IsClean => Stale.Count == 0 && Absent.Count == 0 && Deleted.Count == 0;

   public DiskState Without(IReadOnlyCollection<string> files) =>
      new DiskState(Stale.Except(files).ToList(), Absent.Except(files).ToList(), Deleted.Except(files).ToList());

   public DiskState With(DiskState other) =>
      new DiskState(Stale.Concat(other.Stale).ToList(), Absent.Concat(other.Absent).ToList(), Deleted.Concat(other.Deleted).ToList());
}
