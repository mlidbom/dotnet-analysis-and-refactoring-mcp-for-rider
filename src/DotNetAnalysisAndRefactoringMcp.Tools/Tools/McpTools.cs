using System.Collections.Generic;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.CodeCleanup;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>The tools a solution is served with.</summary>
static class McpTools
{
   /// <summary>The tools, created anew for every request: one kept would keep its build loaded after it was swapped out.</summary>
   public static IReadOnlyList<IMcpTool> Of(ISolution solution) =>
   [
      new FindUsagesTool(solution),
      new GetSymbolInfoTool(solution),
      new FindImplementationsTool(solution),
      new GetFileErrorsTool(solution),
      new SearchSymbolTool(solution),
      new GoToDefinitionTool(solution),
      new GetSolutionStructureTool(solution),
      new BrowseNamespaceTool(solution),
      new ListSymbolsInFileTool(solution),
      new FixUsingsTool(solution),
      new FormatFileTool(solution, solution.GetComponent<CodeCleanupSettingsComponent>()),
      new FlowTool(solution),
      new GetSymbolSourceTool(solution),
      new GetCallHierarchyTool(solution),
      new GetTypeHierarchyTool(solution),
      new GetDiagnosticsTool(solution),
      new InspectCodeTool(solution),
      new ReloadProjectsTool(solution),
      new MoveTypeToNamespaceTool(solution),
      new ListQuickFixesTool(solution),
      new ApplyQuickFixTool(solution),
      new RenameSymbolTool(solution),
      new GenerateMembersTool(solution),
      new CompleteAtTool(solution),
      new ApplySuggestionsTool(solution)
   ];
}
