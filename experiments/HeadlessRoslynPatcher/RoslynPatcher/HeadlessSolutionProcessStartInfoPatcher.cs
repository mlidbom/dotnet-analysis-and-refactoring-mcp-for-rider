using JetBrains.Application.Parts;
using JetBrains.Application.Processes;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.DotNetCore;
using JetBrains.ReSharperAutomationTools.CommandLine.Common.Console.BatchTool.MsBuild;
using JetBrains.ReSharperAutomationTools.CommandLine.Common.Console.BatchTool.Settings;
using JetBrains.Roslyn.Host.ExternalProcess;
using JetBrains.Util;

namespace Royder.Headless.RoslynPatcher;

/// <summary>Starts the Roslyn worker outside the solution directory so Git can remove the
/// worktree while ReSharper is running.</summary>
/// <remarks>The headless launcher gives each engine a private temporary directory outside its worktree.
/// <see cref="RoslynExternalProcess"/> initially sets the worker's current directory to the solution directory.</remarks>
[SolutionInstanceComponent(Instantiation.DemandAnyThreadSafe)]
public sealed class HeadlessSolutionProcessStartInfoPatcher(
   ILogger logger, DotNetCorePlatformsInRuntimeConfigJson cache, ISolutionToolset toolset, ICommandLineToolSettings? settings = null)
   : CommandLineSolutionProcessStartInfoPatcher(logger, cache, toolset, settings)
{
   static readonly string BundledRoslynWorkerExecutable = Path.Combine(
      Path.GetDirectoryName(typeof(RoslynExternalProcess).Assembly.Location)!, "JetBrains.Roslyn.Worker.exe");

   public override ProcessStartInfoPatchResult Patch(JetProcessStartInfo info, JetProcessRuntimeRequest request)
   {
      if(!string.Equals(info.FileName, BundledRoslynWorkerExecutable, StringComparison.OrdinalIgnoreCase))
         return base.Patch(info, request);

      var workerStartInfo = JetProcessStartInfo.CopyFrom(info);
      workerStartInfo.ToProcessStartInfo().WorkingDirectory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "RoslynWorker")).FullName;
      return base.Patch(workerStartInfo, request);
   }
}
