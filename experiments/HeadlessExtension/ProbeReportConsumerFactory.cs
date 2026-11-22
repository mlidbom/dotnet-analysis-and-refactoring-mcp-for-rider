using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Application.FileSystemTracker;
using JetBrains.Application.Parts;
using JetBrains.Application.Threading;
using JetBrains.CommandLine.InspectCode.Unattended;
using JetBrains.CommandLine.InspectCode.Unattended.Settings;
using JetBrains.ProjectModel;
using JetBrains.Threading;

namespace Royder.HeadlessExtension;

/// <summary>Hold the dispatcher for the startup probe, or supply a <see cref="ReadToolComparison"/>
/// consumer that holds it after InspectCode finishes its own analysis.</summary>
[SolutionComponent(Instantiation.DemandAnyThreadSafe)]
public sealed class ProbeReportConsumerFactory(IFileSystemTracker fileSystemTracker, IShellLocks shellLocks, ISolution solution) : IInspectCodeConsumerFactory
{
   public IInspectCodeConsumer? CreateConsumer(IEnumerable<IProjectModelElement> inspectScope,
                                               InspectCodeOutputFormat format, bool useAbsolutePaths, TextWriter? writer)
   {
      var directory = Environment.GetEnvironmentVariable("ROYDER_PROBE_DIRECTORY")!;
      if(Environment.GetEnvironmentVariable("ROYDER_COMPARE_READ_TOOLS") == "1")
         return new ReadToolComparison(solution, directory);

      var stopFile = Path.Combine(directory, "stop");
      shellLocks.ReentrancyGuard.Execute("Check InspectCode write lock", () =>
         File.WriteAllLines(Path.Combine(directory, "waiting.txt"),
                            [$"IFileSystemTracker.Enabled = {fileSystemTracker.Enabled}", CheckWriteLock()]));
      JetDispatcher.Run(() => !File.Exists(stopFile), TimeSpan.FromSeconds(45), bThrowOnTimeout: true);
      return null;
   }

   string CheckWriteLock()
   {
      using var writeLock = shellLocks.UsingWriteLock();
      return "Write lock acquired.";
   }
}
