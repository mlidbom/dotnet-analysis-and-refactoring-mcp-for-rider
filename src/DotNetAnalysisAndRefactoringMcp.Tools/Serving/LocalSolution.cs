using System;
using System.Collections.Generic;
using System.Linq;
using DotNetAnalysisAndRefactoringMcp.Protocol;
using DotNetAnalysisAndRefactoringMcp.Tools;
using JetBrains.Util;
using JetBrains.Util.Logging;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>A solution open in this Rider, whose calls this tools build answers.</summary>
sealed class LocalSolution(IMcpServer server, IMcpSolution solution) : OpenSolution(solution.Name, solution.FilePath)
{
   static readonly ILogger Log = Logger.GetLogger<LocalSolution>();

   readonly IMcpServer _server = server;
   readonly IMcpSolution _solution = solution;

   public override IReadOnlyList<ToolDefinition> Tools =>
      McpTools.Of(_solution.Solution)
              .Select(tool => new ToolDefinition
                              {
                                 Name = tool.Name,
                                 Description = tool.Description,
                                 InputSchema = JsonOf.Value(tool.InputSchema)
                              })
              .ToList();

   public override JsonRpcResponse Call(JsonRpcRequest request, string tool, JObject arguments)
   {
      var called = McpTools.Of(_solution.Solution).FirstOrDefault(it => it.Name == tool);
      if(called == null)
         return request.ToolError($"Unknown tool: {tool}");

      try
      {
         return request.ToolAnswer(new ToolCall(_solution, called).Answer(arguments));
      }
      catch(Exception exception)
      {
         // Warn, not Error: errors surface as IDE exception balloons, and a failing tool call is the caller's news.
         // As text: an exception kept keeps loaded every tools build its stack trace runs through.
         Log.Warn($"DotNetAnalysisAndRefactoringMcp tool '{tool}' failed: {exception}");
         return request.ToolError(ToolFailure.Line(exception, tool));
      }
   }

   public override JObject Health()
   {
      var health = NameAndPath();
      try
      {
         health["indexing"] = new Indexing(_solution.Solution).IsRunning;
         health["pendingDiskChanges"] = _solution.Changes.Count + _solution.DiskSyncState.Locked(it => it.Stale.Count);
         health["tools"] = _server.ToolsBuilds;
      }
      catch(Exception exception)
      {
         // The solution's state is unknown, which must not read as ready: OpenSolutions.Health reads it as indexing.
         health["healthFailed"] = ToolFailure.Logged(exception, $"the health of '{Name}'");
      }

      return health;
   }
}
