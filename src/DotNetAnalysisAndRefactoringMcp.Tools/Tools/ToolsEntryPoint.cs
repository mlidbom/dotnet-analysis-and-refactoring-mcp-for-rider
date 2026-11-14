using System.Collections.Generic;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.CodeCleanup;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   What the host creates first when it loads a build of the tools: it knows this type by its name,
///   <see cref="McpToolsEntryPoint.TypeName"/>.
/// </summary>
public sealed class ToolsEntryPoint : IMcpToolsEntryPoint
{
  public string Instructions => McpInstructions.Text;

  public IReadOnlyList<IMcpTool> CreateTools(ISolution solution) =>
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
