using System.Text.Json;
using JetBrains.Application.Environment;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Refactorings;
using JetBrains.ReSharper.Feature.Services.Refactorings.Specific.Rename;
using JetBrains.ReSharper.Psi.CSharp;
using JetBrains.ReSharper.Refactorings.Move.MoveToNamespace;

namespace HeadlessHost;

/// <summary>Identifies the language helpers selected by the real rename and move workflows in this host.</summary>
static class CSharpRefactoringSupport
{
   public static void Print(ISolution solution)
   {
      ZoneAvailability.Print(solution);
      var zones = solution.GetComponent<RunsProducts.ProductConfigurations>().RunningZones;
      var rename = RenameRefactoringService.Instance.GetRenameService(CSharpLanguage.Instance);
      var move = new MoveToNamespaceWorkflow(solution, "Experiment").Helper[CSharpLanguage.Instance];
      Console.WriteLine("REFACTORINGS " + JsonSerializer.Serialize(new {
         zones = zones.Select(it => it.ZoneInterfaceType.LocalName.ToString()).Order(),
         rename = new { helper = rename.GetType().FullName, rename.IsLanguageSupported },
         move = new { helper = move.GetType().FullName, move.IsLanguageSupported }
      }));
   }
}
