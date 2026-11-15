using System.Collections.Generic;
using System.Linq;
using DotNetAnalysisAndRefactoringMcp.Protocol;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.CodeCleanup;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   What the host creates first when it loads a build of the tools: it knows this type by its name,
///   <see cref="McpToolsEntryPoint.TypeName"/>.
/// </summary>
public sealed class ToolsEntryPoint : IMcpToolsEntryPoint
{
   public string Instructions => McpInstructions.Text;

   public List<ToolDefinition> ListTools(ISolution solution) =>
      CreateTools(solution).Select(tool => new ToolDefinition
                                           {
                                              Name = tool.Name,
                                              Description = tool.Description,
                                              InputSchema = JsonOf.Value(tool.InputSchema)
                                           })
                           .ToList();

   public string CallTool(IMcpSolution solution, string name, JObject arguments)
   {
      var tool = CreateTools(solution.Solution).FirstOrDefault(it => it.Name == name);
      return tool == null ? null : new ToolCall(solution, tool).Answer(arguments);
   }

   /// <summary>The tools, created anew for every call: one kept would keep its build loaded after it was swapped out.</summary>
   static IReadOnlyList<IMcpTool> CreateTools(ISolution solution) =>
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
