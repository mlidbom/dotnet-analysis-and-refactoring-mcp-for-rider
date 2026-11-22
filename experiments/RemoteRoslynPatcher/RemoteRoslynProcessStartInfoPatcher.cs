using System;
using System.IO;
using JetBrains.Application.Parts;
using JetBrains.Application.Processes;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.DotNetCore;
using JetBrains.RdBackend.Common.Features.Processes;
using JetBrains.Roslyn.Host.ExternalProcess;
using JetBrains.Util;

namespace Royder.RemoteRoslynPatcher;

/// <summary>Gives the local <see cref="RoslynExternalProcess"/> a working directory outside the
/// worktree. The agent server supplies that directory in its own process environment.</summary>
[SolutionInstanceComponent(Instantiation.DemandAnyThreadSafe)]
public sealed class RemoteRoslynProcessStartInfoPatcher(
   ILogger logger, ISolutionToolset solutionToolset, IRiderProcessStartInfoEnvironment environment,
   DotNetCorePlatformsInRuntimeConfigJson cache)
   : RiderProcessStartInfoPatcher(logger, solutionToolset, environment, cache)
{
   const string WorkingDirectoryVariable = "ROYDER_REMOTE_ROSLYN_WORKING_DIRECTORY";

   static readonly string LocalRoslynWorker =
      (typeof(RoslynExternalProcess).Assembly.GetDirectory() / "JetBrains.Roslyn.Worker.exe").FullPath;

   public override ProcessStartInfoPatchResult Patch(JetProcessStartInfo info, JetProcessRuntimeRequest request)
   {
      var directory = Environment.GetEnvironmentVariable(WorkingDirectoryVariable);
      if(string.IsNullOrEmpty(directory) || !InteractionContext.SolutionContext.IsLocal()
         || !string.Equals(info.FileName, LocalRoslynWorker, StringComparison.OrdinalIgnoreCase))
         return base.Patch(info, request);

      if(!Path.IsPathFullyQualified(directory))
         throw new ArgumentException($"{WorkingDirectoryVariable} must name an absolute directory outside the worktree.");

      var worker = JetProcessStartInfo.CopyFrom(info);
      worker.ToProcessStartInfo().WorkingDirectory = Directory.CreateDirectory(directory).FullName;
      Logger.Info($"RemoteRoslynProcessStartInfoPatcher selected Roslyn working directory '{directory}'.");
      return base.Patch(worker, request);
   }
}
