#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Application.Threading;
using JetBrains.Application.Threading.Tasks;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.Rd.Tasks;
using JetBrains.ReSharper.Feature.Services.Protocol;
using JetBrains.Rider.Model;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   Rider's frontend asked to rescan paths on disk, as it does when its file watcher sees a change: new files reach the
///   project model, and changed ones Rider's copies, some time after.
/// </summary>
/// <remarks>
///   It queues them (debounced by 200 ms) and returns. Without a frontend, or when it doesn't answer in time, a request
///   does nothing, and whatever checks the result afterwards still sees what wasn't rescanned.
/// </remarks>
sealed class Rescans(ISolution solution, IShellLocks locks)
{
   static readonly ILogger Log = Logger.GetLogger<Rescans>();

   static readonly TimeSpan RefreshTimeout = TimeSpan.FromSeconds(5);

   readonly ISolution _solution = solution;
   readonly IShellLocks _locks = locks;

   public async Task Request(Lifetime lifetime, IReadOnlyCollection<string> paths)
   {
      try
      {
         var refresh = _locks.Tasks.StartNew(lifetime,
                                             Scheduling.MainGuard,
                                             () =>
                                             {
                                                var request = RefreshRequests.New(paths);

                                                return _solution.GetProtocolSolution().GetFileSystemModel().RefreshPaths.Start(lifetime, request).AsTask();
                                             });

         // WhenAny hands back a faulted task without throwing; await it to see why.
         if(await Task.WhenAny(refresh, Task.Delay(RefreshTimeout)).ConfigureAwait(false) == refresh)
            await refresh.ConfigureAwait(false);
      }
      catch(Exception exception) when(exception is not OperationCanceledException)
      {
         Log.Warn(exception, ExceptionOrigin.Algorithmic, "DotNetAnalysisAndRefactoringMcp could not ask Rider to rescan changed files");
      }
   }
}
