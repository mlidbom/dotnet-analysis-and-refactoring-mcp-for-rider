using System.Collections.Generic;
using DotNetAnalysisAndRefactoringMcp.Protocol;
using JetBrains.ProjectModel;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   The way into a build of the tools assembly: the host creates the type <see cref="McpToolsEntryPoint.TypeName"/> names when it loads the build,
///   and asks it to list and call its tools.
/// </summary>
/// <remarks>
///   What crosses is the host's types and plain data, never an object of the build: one the host kept would keep the
///   build loaded after it was swapped out. A call's whole flow is the build's: the disk sync before it, the dispatch by
///   the tool's kind, the timeout, the notes (until 2026-11-15 it was the host's, and changing it took a Rider restart).
/// </remarks>
public interface IMcpToolsEntryPoint
{
   /// <summary>The tools a solution's calls are served with, described in the host's types.</summary>
   List<ToolDefinition> ListTools(ISolution solution);

   /// <summary>The answer of the tool <paramref name="name"/>, or <c>null</c> when the build has no tool by that name.</summary>
   string CallTool(IMcpSolution solution, string name, JObject arguments);

   /// <summary>The usage guide clients get when they connect, in the <c>initialize</c> response.</summary>
   string Instructions { get; }
}

public static class McpToolsEntryPoint
{
   /// <summary>The type in the tools assembly that implements <see cref="IMcpToolsEntryPoint"/>.</summary>
   public const string TypeName = "DotNetAnalysisAndRefactoringMcp.Tools.ToolsEntryPoint";

   /// <summary>The tools assembly's file name, in every build folder.</summary>
   public const string AssemblyFileName = "DotNetAnalysisAndRefactoringMcp.Tools.dll";
}
