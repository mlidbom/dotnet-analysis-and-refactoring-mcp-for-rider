using System.IO;
using System.Linq;
using JetBrains.Application.Settings;
using JetBrains.Application.Settings.Implementation;
using JetBrains.Application.Settings.UserInterface;
using JetBrains.Application.Threading;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Daemon;
using JetBrains.ReSharper.Daemon.Impl;
using JetBrains.ReSharper.Daemon.SolutionAnalysis;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.CSharp.CodeStyle.FormatSettings;
using JetBrains.Util;

namespace Royder.HeadlessExtension;

/// <summary>Read the actual settings mounts and the C# settings in effect for McpShellComponent.cs.</summary>
sealed class SettingsProbe(ISolution solution)
{
   public void Record(string directory)
   {
      using var read = solution.Locks.UsingReadLock();
      var projectFile = solution.GetAllProjects().SelectMany(it => it.GetAllProjectFiles())
                                .Single(it => it.Name == "McpShellComponent.cs");
      var source = projectFile.ToSourceFiles().First();
      var settings = source.GetSettingsStoreWithEditorConfig(solution);
      var analysis = SolutionAnalysisConfiguration.GetInstance(solution);
      if(analysis.Completed.Value)
      {
         var issues = SolutionAnalysisManager.GetInstance(solution).IssueSet.AllIssues;
         issues.RunOperationWithLock(() =>
            File.WriteAllLines(Path.Combine(directory, "cached-issues.txt"),
                               issues.GetIssues(source.Ptr()).Select(it => $"{it.IssueType.ConfigurableSeverityId} {it.GetSeverity()}")));
      }
      File.WriteAllLines(Path.Combine(directory, "effective.txt"), [
         $"WarningsMode = {analysis.WarningsMode.Value}",
         $"IsSwaEnabled = {solution.GetComponent<DaemonEnablePolicy>().IsSwaEnabled(source)}",
         $"INDENT_SIZE = {settings.GetValue((CSharpFormatSettingsKey it) => it.INDENT_SIZE)}",
         $"SPACE_BEFORE_IF_PARENTHESES = {settings.GetValue((CSharpFormatSettingsKey it) => it.SPACE_BEFORE_IF_PARENTHESES)}"
      ]);
      var mounts = solution.GetComponent<SettingsStorageMountPoints>().EnumMountPointsLiveOrdered;
      File.WriteAllLines(Path.Combine(directory, "mounts.tsv"), mounts.Select(it =>
      {
         it.Metadata.TryGet(UserFriendlySettingsLayers.LayerId, out var id);
         it.Metadata.TryGet(UserFriendlySettingsLayers.DiskFilePath, out var path);
         return $"{it.Priority}\t{it.Flags}\t{id}\t{it.Name}\t{path}";
      }));
   }
}
