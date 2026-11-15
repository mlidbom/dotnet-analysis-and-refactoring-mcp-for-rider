using System;
using System.Collections.Generic;

namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   What the disk sync, which runs in the tools before every call, remembers from one call to the next: the files it
///   found stale or absent, with their disk times then, and whether it watches the projects' directories.
/// </summary>
/// <remarks>
///   In the host, so that it outlives a tools build: a file still stale after the wait isn't waited on again until its
///   disk time changes, and that must hold across a swap too. Shared by the calls in progress, through an
///   <see cref="Threading.IThreadShared{TShared}"/>.
/// </remarks>
public sealed class DiskSyncState
{
   /// <summary>Files whose Rider copy still differed from disk after the wait, and their disk times then.</summary>
   public readonly Dictionary<string, DateTime> Stale = new(StringComparer.Ordinal);

   /// <summary>Named files that were on disk but in no project, and their disk times then.</summary>
   public readonly Dictionary<string, DateTime> Absent = new(StringComparer.Ordinal);

   /// <summary>Whether the projects' directories are watched already: that's done once.</summary>
   public bool WatchesProjects;
}
