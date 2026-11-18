using System.Collections.Generic;
using DotNetAnalysisAndRefactoringMcp.Disk;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
/// Marker for tools that edit source files. They are refused while Rider's copy of a file differs from disk,
/// because the edit would be applied to the old text and saved over the newer one. A call with
/// <c>dryRun: true</c> edits nothing and is never refused.
/// </summary>
/// <remarks>
/// Orthogonal to <see cref="IMcpWriteTool"/>, <see cref="IMcpMainThreadTool"/> and <see cref="IMcpAsyncTool"/>, which
/// choose a thread and a lock.
/// </remarks>
public interface IMcpEditTool : IMcpTool {}

/// <summary>
/// An edit tool whose edit moves files too: on disk, once Rider has saved the edit, by the call (<see cref="FileMoves"/>),
/// which adds what became of each to the answer.
/// </summary>
public interface IMcpFileMovingTool : IMcpEditTool
{
   /// <summary>The files the last <see cref="IMcpTool.Execute"/> moves; none for a dry run, which says where they'd go.</summary>
   IReadOnlyList<FileMove> FileMoves { get; }
}

/// <summary>
/// Marker for tools whose arguments name more files than they list, e.g. "every file changed since a git ref".
/// <see cref="Expand"/> runs before any lock is taken, so it may touch the file system or start processes.
/// </summary>
public interface IMcpExpandableTool : IMcpTool
{
   JObject Expand(JObject arguments);
}
