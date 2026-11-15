using System.Collections.Generic;
using JetBrains.ProjectModel;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   The way into a build of the tools assembly: the host creates the type <see cref="McpToolsEntryPoint.TypeName"/> names when it loads the build,
///   and asks it for the tools at each call.
/// </summary>
public interface IMcpToolsEntryPoint
{
   /// <summary>
   ///   The tools for one call. They are created anew at every call, and the host keeps none of them afterwards: an
   ///   instance kept would keep its build loaded after it was swapped out.
   /// </summary>
   IReadOnlyList<IMcpTool> CreateTools(ISolution solution);

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
