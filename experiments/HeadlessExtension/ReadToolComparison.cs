using System;
using System.IO;
using JetBrains.Application.Components;
using JetBrains.Application.Settings;
using JetBrains.CommandLine.InspectCode.Unattended;
using JetBrains.DocumentModel.Storage;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.ProjectsHost;
using JetBrains.ProjectModel.ProjectsHost.SolutionHost;
using JetBrains.ReSharper.Daemon.SolutionAnalysis;
using JetBrains.ReSharper.Daemon;
using JetBrains.ReSharper.ExternalSources.MetadataViewer;
using JetBrains.ReSharper.Feature.Services.CodeCleanup;
using JetBrains.ReSharper.Feature.Services.ExternalSources.MetadataTranslator;
using JetBrains.ReSharper.Feature.Services.Refactorings.Specific.Rename;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.TextControl;
using JetBrains.Threading;

namespace Royder.HeadlessExtension;

/// <summary>Hold InspectCode's solution open after its inspection has run, when solution-wide results are available.</summary>
public sealed class ReadToolComparison(ISolution solution, string directory) : IInspectCodeConsumer
{
   public void Consume(IIssue issue) { }

   public void Dispose()
   {
      var analysis = SolutionAnalysisConfiguration.GetInstance(solution);
      if(Environment.GetEnvironmentVariable("ROYDER_SETTINGS_PROBE") == "1")
         solution.Locks.ReentrancyGuard.Execute("Read settings and solution analysis results", () => new SettingsProbe(solution).Record(directory));
      File.WriteAllLines(Path.Combine(directory, "components.txt"), [
         Describe(nameof(IDocumentStorageHelpers), () => solution.GetComponent<IDocumentStorageHelpers>()),
         Describe(nameof(ISolutionHost), () => solution.ProjectsHostContainer().GetComponent<ISolutionHost>()),
         Describe(nameof(IPsiCachesState), () => solution.GetComponent<IPsiCachesState>()),
         Describe(nameof(ITextControlManager), () => solution.GetComponent<ITextControlManager>()),
         Describe(nameof(CodeCleanupSettingsComponent), () => solution.GetComponent<CodeCleanupSettingsComponent>()),
         Observe("C# metadata translator", MetadataTranslator),
         Observe("C# rename service", RenameService),
         Observe("DecompilerLegalNoticeAccepted", () => solution.GetComponent<ISettingsStore>()
            .BindToContextTransient(ContextRange.ApplicationWide).GetValue((DecompilerSettings it) => it.DecompilerLegalNoticeAccepted).ToString()),
         $"SolutionAnalysisConfiguration.Enabled = {analysis.Enabled.Value}",
         $"SolutionAnalysisConfiguration.Completed = {analysis.Completed.Value}"
      ]);
      File.WriteAllText(Path.Combine(directory, "waiting.txt"), "InspectCode has finished inspecting.");
      var stop = Path.Combine(directory, "stop");
      JetDispatcher.Run(() => !File.Exists(stop), TimeSpan.FromMinutes(25), bThrowOnTimeout: true);
   }

   static string MetadataTranslator()
   {
      var translator = LanguageManager.Instance.TryGetService<IMetadataTranslator>(CSharpLanguage.Instance);
      return translator == null ? "absent" : $"{translator.GetType().FullName}, available = {translator.IsAvailable}";
   }

   static string RenameService()
   {
      var service = RenameRefactoringService.Instance.GetRenameService(CSharpLanguage.Instance);
      return $"{service.GetType().FullName}, language supported = {service.IsLanguageSupported}";
   }

   static string Describe(string service, Func<object> resolve) => Observe(service, () => resolve().GetType().FullName!);

   static string Observe(string name, Func<string> read)
   {
      try { return $"{name} = {read()}"; }
      catch(Exception exception) { return $"{name} failed: {exception.GetType().Name}: {exception.Message.Split('\n')[0]}"; }
   }
}
