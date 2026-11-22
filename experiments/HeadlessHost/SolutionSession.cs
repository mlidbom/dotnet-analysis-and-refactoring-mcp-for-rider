using System.Diagnostics;
using JetBrains.Application.FileSystemTracker;
using JetBrains.Application.Threading;
using JetBrains.ProjectModel;
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
      var elapsed = DateTime.UtcNow - process.StartTime.ToUniversalTime();
      Console.WriteLine($"INDEXED pid={process.Id} seconds={elapsed.TotalSeconds:F3} " +
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
         else
            FindUsages(input.Result);
      }
   }
}
