using System.Diagnostics;
using System.Text.Json;
using JetBrains.Application.FileSystemTracker;
using JetBrains.Application.Threading;
using JetBrains.DocumentManagers.impl;
using JetBrains.DocumentModel.Storage;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.Threading;
using JetBrains.Util;

namespace HeadlessHost;

/// <summary>Queries and measures one open <see cref="ISolution"/> on the ReSharper main thread.</summary>
sealed class SolutionSession(ISolution solution, IShellLocks locks)
{
   readonly IFileSystemTracker _tracker = solution.GetComponent<IFileSystemTracker>();
   readonly UsageSearch _search = new(solution);

   public void MeasureWhenIndexed()
   {
      var caches = solution.GetComponent<IPsiCachesState>();
      JetDispatcher.Run(() => !caches.IsIdle.Value, TimeSpan.FromMinutes(3), bThrowOnTimeout: true);
      using var process = Process.GetCurrentProcess();
      var started = new DateTimeOffset(process.StartTime);
      var elapsed = DateTimeOffset.Now - started;
      Console.WriteLine($"INDEXED pid={process.Id} started={started:O} seconds={elapsed.TotalSeconds:F3} " +
                        $"privateBytes={process.PrivateMemorySize64} workingSetBytes={process.WorkingSet64}");
      locks.ReentrancyGuard.Execute("Count projects", () =>
      {
         using var read = locks.UsingReadLock();
         var projects = solution.GetAllProjects().Count(it => !it.ProjectFileLocation.IsNullOrEmpty());
         Console.WriteLine($"PROJECTS {projects} tracker={_tracker.GetType().Name} enabled={_tracker.Enabled} autoCommit={_tracker.AutoCommitAllowed.Value}");
      });
   }

   public bool FindUsages(string symbol) => locks.ReentrancyGuard.Execute("Find usages", symbol, query =>
   {
      using var read = locks.UsingReadLock();
      return _search.Print(query);
   });

   /// <summary>Keeps the dispatcher running between input lines so queued file changes have a chance to arrive.</summary>
   public void Listen()
   {
      while(true)
      {
         Console.WriteLine("READY");
         var input = Task.Run(Console.ReadLine);
         JetDispatcher.Run(input, TimeSpan.MaxValue, bThrowOnTimeout: true);
         if(input.Result is null or "exit") return;
         if(input.Result == "refresh")
            locks.ReentrancyGuard.Execute("Commit file system changes", () => _tracker.CommitChanges());
         else if(input.Result == "refactorings")
            locks.ReentrancyGuard.Execute("Read C# refactoring support", () =>
            {
               using var read = locks.UsingReadLock();
               CSharpRefactoringSupport.Print(solution);
            });
         else if(input.Result.StartsWith("document ", StringComparison.Ordinal))
            PrintDocument(input.Result["document ".Length..]);
         else
            FindUsages(input.Result);
      }
   }

   /// <summary>Reads a project document directly, without Royder's pre-call disk synchronization.</summary>
   void PrintDocument(string path) => locks.ReentrancyGuard.Execute("Read experiment document", () =>
   {
      using var read = locks.UsingReadLock();
      var file = VirtualFileSystemPath.Parse(path, InteractionContext.SolutionContext);
      var items = solution.FindProjectItemsByLocation(file).OfType<IProjectFile>().ToArray();
      var document = items.Select(it => it.ToSourceFiles().FirstOrDefault()).FirstOrDefault(it => it != null)?.Document;
      var storage = solution.GetComponent<IDocumentStorageHelpers>();
      Console.WriteLine("DOCUMENT " + JsonSerializer.Serialize(new {
         projectItems = items.Length, text = document?.GetText(), storage = storage.GetType().FullName,
         unsaved = document != null && ((DocumentOperationsImpl)storage).IsDocumentUnsaved(document)
      }));
   });
}
