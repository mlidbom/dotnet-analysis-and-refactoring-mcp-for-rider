using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

public interface IMcpTool
{
   string Name { get; }
   string Description { get; }
   ToolAnnotations Annotations { get; }
   object InputSchema { get; }
   object Execute(JObject arguments);
}
