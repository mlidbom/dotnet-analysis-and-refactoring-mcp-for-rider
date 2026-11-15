using DotNetAnalysisAndRefactoringMcp.Serving;
using Newtonsoft.Json;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   What the host creates first when it loads a build of the tools: it knows this type by its name,
///   <see cref="McpToolsEntryPoint.TypeName"/>.
/// </summary>
public sealed class ToolsEntryPoint : IMcpToolsEntryPoint
{
   public string Answer(IMcpServer server, string request) => McpRequest.Answer(server, request);

   public string Health(IMcpServer server) => OpenSolutions.Of(server).Health().ToString(Formatting.None);

   public void RegisterWithThePrimary(IMcpServer server, IMcpSolution solution) => new PrimaryServer(server).Register(new LocalSolution(server, solution));

   public void DeregisterFromThePrimary(IMcpServer server, IMcpSolution solution) => new PrimaryServer(server).Deregister(solution);
}
