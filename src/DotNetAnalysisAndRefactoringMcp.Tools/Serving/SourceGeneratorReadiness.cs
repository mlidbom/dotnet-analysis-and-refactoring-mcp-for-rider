#nullable enable
using System.Linq;
using JetBrains.Application.Threading;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.Impl;
using JetBrains.ProjectModel.ProjectsHost.SolutionHost;
using JetBrains.ProjectModel.Properties;
using JetBrains.ReSharper.Feature.Services.Protocol;
using JetBrains.Rider.Model;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>Reports when Rider's Roslyn worker has synchronized its project model and ReSharper has applied
/// its source-generator results, including a result with no generated documents.</summary>
static class SourceGeneratorReadiness
{
   /// <returns>True when synchronized, false while readiness is unconfirmed, or null when the loaded
   /// <paramref name="solution"/> has no analyzer references in projects that support source generators.</returns>
   public static bool? Of(ISolution solution)
   {
      bool? ready = false;
      solution.Locks.TryExecuteWithReadLock(() => ready = ReadUnderLock(solution));
      return ready;
   }

   static bool? ReadUnderLock(ISolution solution)
   {
      var model = solution.TryGetComponent<IProjectModelMonitor>();
      if(model?.Loaded.Value != true || !model.Ready.Value) return false;
      if(!solution.GetAllProjects().Any(HasApplicableAnalyzerReferences)) return null;

      var generators = solution.TryGetComponent<ISourceGeneratorMonitor>();
      if(generators?.Initialized.Value != true) return false;

      // Rider also initializes the generator monitor when Roslyn is disabled. Its presenter publishes
      // RoslynReady after worker synchronization. Read the existing model without creating a protocol extension.
      var worker = solution.GetProtocolSolution().GetExtension<RoslynFrontendModel>("roslynFrontendModel");
      return generators.IsUpToDate.Value && worker?.RoslynReady.Maybe.ValueOrDefault == true;
   }

   static bool HasApplicableAnalyzerReferences(IProject project)
   {
      if(!project.IsProjectFromUserView() || project.IsSolutionFolder() || project.IsSharedProject()) return false;
      var language = project.ProjectProperties.DefaultLanguage;
      if(language != ProjectLanguage.CSHARP && language != ProjectLanguage.VBASIC) return false;
      return project.TargetFrameworkIds.Any(framework => project.GetModuleReferences(framework).OfType<IRoslynAnalyzerReference>().Any());
   }
}
