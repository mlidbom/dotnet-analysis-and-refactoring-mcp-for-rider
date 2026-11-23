using System.Collections.Generic;
using DotNetAnalysisAndRefactoringMcp.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Protocol;

public class InitializeResult
{
   [JsonProperty("protocolVersion")]
   public string ProtocolVersion { get; set; } = "2025-03-26";

   [JsonProperty("capabilities")]
   public ServerCapabilities Capabilities { get; set; } = new ServerCapabilities();

   [JsonProperty("serverInfo")]
   public ServerInfo ServerInfo { get; set; } = new ServerInfo();

   [JsonProperty("instructions")]
   public string Instructions { get; set; }
}

public class ServerCapabilities
{
   [JsonProperty("tools")]
   public ToolsCapability Tools { get; set; } = new ToolsCapability();
}

public class ToolsCapability
{
   /// <summary>The server sends <c>notifications/tools/list_changed</c> when it swaps in a new tools build.</summary>
   [JsonProperty("listChanged")]
   public bool ListChanged { get; set; } = true;
}

public class ServerInfo
{
   [JsonProperty("name")]
   public string Name { get; set; } = "dotnet-analysis-and-refactoring-mcp";

   [JsonProperty("version")]
   public string Version { get; set; } = "0.13.0";
}

public class ToolsListResult
{
   [JsonProperty("tools")]
   public List<ToolDefinition> Tools { get; set; } = new List<ToolDefinition>();
}

public class ToolDefinition
{
   [JsonProperty("name")]
   public string Name { get; set; }

   [JsonProperty("description")]
   public string Description { get; set; }

   [JsonProperty("inputSchema")]
   public object InputSchema { get; set; }

   [JsonProperty("annotations", NullValueHandling = NullValueHandling.Ignore)]
   public object Annotations { get; set; }

   /// <summary>
   ///   MCP's <c>_meta</c>: what particular clients read about the tool. So far only Claude Code's
   ///   <c>anthropic/alwaysLoad</c>, for an <see cref="IMcpAlwaysLoadedTool"/>.
   /// </summary>
   [JsonProperty("_meta", NullValueHandling = NullValueHandling.Ignore)]
   public object Meta { get; set; }

   /// <summary>The <c>_meta</c> of <paramref name="tool"/>: none, unless Claude Code is to load it up front.</summary>
   public static JObject MetaOf(IMcpTool tool) =>
      tool is IMcpAlwaysLoadedTool ? new JObject { ["anthropic/alwaysLoad"] = true } : null;
}

public class CallToolResult
{
   [JsonProperty("content")]
   public List<ContentBlock> Content { get; set; } = new List<ContentBlock>();

   [JsonProperty("isError")]
   public bool IsError { get; set; }
}

public class ContentBlock
{
   [JsonProperty("type")]
   public string Type { get; set; } = "text";

   [JsonProperty("text")]
   public string Text { get; set; }
}
