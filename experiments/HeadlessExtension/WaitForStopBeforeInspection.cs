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

/// <summary>InspectCode asks its report consumers to join once the solution is open.
/// Keep its dispatcher running at that point until the driver has queried Royder.</summary>
[SolutionComponent(Instantiation.DemandAnyThreadSafe)]
public sealed class WaitForStopBeforeInspection(IFileSystemTracker fileSystemTracker, IShellLocks shellLocks) : IInspectCodeConsumerFactory
{
   public IInspectCodeConsumer? CreateConsumer(IEnumerable<IProjectModelElement> inspectScope,
                                               InspectCodeOutputFormat format, bool useAbsolutePaths, TextWriter? writer)
   {
      var directory = Environment.GetEnvironmentVariable("ROYDER_PROBE_DIRECTORY")!;
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
