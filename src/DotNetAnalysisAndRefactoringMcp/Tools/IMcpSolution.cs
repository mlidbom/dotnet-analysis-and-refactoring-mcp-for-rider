using System;
using System.Collections.Generic;
using DotNetAnalysisAndRefactoringMcp.Disk;
using DotNetAnalysisAndRefactoringMcp.Threading;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   An open solution, as the host hands it to a tools build for one call: the solution, and what of it outlives the
///   build, such as the files the watcher saw change.
/// </summary>
public interface IMcpSolution
{
   ISolution Solution { get; }

   /// <summary>The solution's lifetime: a call's own lifetime nests in it.</summary>
   Lifetime Lifetime { get; }

   /// <summary>Where relative paths start.</summary>
   SolutionRoot Root { get; }

   /// <summary>Source files that changed on disk since the last call took them.</summary>
   DiskChanges Changes { get; }

   /// <summary>What the disk sync remembers from one call to the next.</summary>
   IThreadShared<DiskSyncState> DiskSyncState { get; }

   /// <summary>Whether ReSharper is still indexing, so that answers may be incomplete.</summary>
   bool IsIndexing { get; }

   /// <summary>What a caller should know about the tools builds, such as one that failed to load.</summary>
   IReadOnlyList<string> BuildNotes { get; }

   /// <summary>
   ///   Keeps the tools build loaded until the returned object is disposed: for work that outlives the call, such as a
   ///   call that timed out and goes on until it notices.
   /// </summary>
   IDisposable KeepTheBuildLoaded();
}
