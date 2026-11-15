#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DotNetAnalysisAndRefactoringMcp.Threading;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   Source files that changed on disk since they were last taken, collected by watching directories.
/// </summary>
/// <remarks>
///   Rider notices external edits by itself, but late while its window is in the background. This list is what
///   lets a tool call that names no file still catch up with edits made by <c>sed</c>, scripts or heredocs.
///   Only directories that are watched are covered; files a call names are checked regardless.
/// </remarks>
public sealed class DiskChanges : IDisposable
{
   static readonly ILogger Log = Logger.GetLogger<DiskChanges>();

   static readonly HashSet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                                {
                                                   ".cs",
                                                   ".fs",
                                                   ".fsi",
                                                   ".fsx",
                                                   ".vb",
                                                   ".razor",
                                                   ".cshtml",
                                                };

   static readonly HashSet<string> IgnoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                                        {
                                                           "bin",
                                                           "obj",
                                                           ".git",
                                                           ".idea",
                                                           ".vs",
                                                           "node_modules",
                                                           "Library",
                                                           "Temp",
                                                           "Logs",
                                                        };

   readonly IThreadShared<Watched> _watched = IThreadShared.New(new Watched());

   public int Count => _watched.Locked(it => it.Overflowed ? it.Paths.Count + 1 : it.Paths.Count);

   /// <summary>
   ///   Starts watching <paramref name="root"/> recursively, unless a watched root already covers it.
   /// </summary>
   public void Watch(string root)
   {
      var isNew = _watched.Locked(it =>
      {
         if(!Directory.Exists(root) || it.Roots.Any(x => Covers(x, root)))
            return false;

         it.Roots.Add(root);
         return true;
      });
      if(!isNew)
         return;

      try
      {
         var watcher = new FileSystemWatcher(root)
                       {
                          IncludeSubdirectories = true,
                          NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                          InternalBufferSize = 64 * 1024,
                       };
         // Watcher events arrive on a runtime thread where an unhandled exception terminates Rider's backend.
         watcher.Changed += (_, e) => Safely(() => Add(root, e.FullPath));
         watcher.Created += (_, e) => Safely(() => Add(root, e.FullPath));
         watcher.Deleted += (_, e) => Safely(() => Add(root, e.FullPath));
         watcher.Renamed += (_, e) => Safely(() =>
         {
            Add(root, e.OldFullPath);
            Add(root, e.FullPath);
         });
         watcher.Error += (_, e) => Safely(() => Overflow(e.GetException()));
         watcher.EnableRaisingEvents = true;

         _watched.Locked(it => it.Watchers.Add(watcher));
      }
      catch(Exception exception)
      {
         Log.Warn(exception, ExceptionOrigin.Algorithmic, $"DotNetAnalysisAndRefactoringMcp cannot watch '{root}'; only files named by a call will be checked");
      }
   }

   /// <summary>
   ///   Takes the changed files, or the watched roots when the watcher lost track of events.
   /// </summary>
   public DiskChangeSet Take()
   {
      return _watched.Locked(it =>
      {
         var set = new DiskChangeSet(it.Paths.ToList(), it.Overflowed ? it.Roots.ToList() : new List<string>());
         it.Paths.Clear();
         it.Overflowed = false;

         return set;
      });
   }

   public void Dispose()
   {
      _watched.Locked(it =>
      {
         foreach(var watcher in it.Watchers)
            watcher.Dispose();

         it.Watchers.Clear();
      });
   }

   void Safely(Action handle)
   {
      try
      {
         handle();
      }
      catch(Exception exception)
      {
         try
         {
            Overflow(exception);
         }
         catch
         {
            // Nothing may escape a watcher thread, not even a failure to log.
         }
      }
   }

   void Add(string root, string path)
   {
      if(!Extensions.Contains(Path.GetExtension(path)) || IsIgnored(root, path))
         return;

      _watched.Locked(it => it.Paths.Add(path));
   }

   void Overflow(Exception exception)
   {
      Log.Warn(exception, ExceptionOrigin.Algorithmic, "DotNetAnalysisAndRefactoringMcp lost file change events; the next call refreshes the watched roots");
      _watched.Locked(it => it.Overflowed = true);
   }

   static bool IsIgnored(string root, string path)
   {
      var relative = path.Length > root.Length ? path.Substring(root.Length) : "";
      var segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                                    StringSplitOptions.RemoveEmptyEntries);

      return segments.Take(segments.Length - 1).Any(IgnoredDirectories.Contains);
   }

   static bool Covers(string root, string path)
   {
      var prefix = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

      return string.Equals(root, path, StringComparison.OrdinalIgnoreCase)
          || path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
   }

   /// <summary>
   ///   What the watchers' threads and the calls share: the watched roots and their watchers, the files changed since
   ///   last taken, and whether events were lost.
   /// </summary>
   sealed class Watched
   {
      public readonly List<FileSystemWatcher> Watchers = [];
      public readonly List<string> Roots = [];
      public readonly HashSet<string> Paths = new(StringComparer.Ordinal);
      public bool Overflowed;
   }
}

/// <summary>
///   What <see cref="DiskChanges.Take"/> returned: files that changed, and directories to rescan whole when
///   change events were lost.
/// </summary>
public sealed class DiskChangeSet
{
   public DiskChangeSet(IReadOnlyList<string> files, IReadOnlyList<string> directories) =>
      (Files, Directories) = (files, directories);

   public IReadOnlyList<string> Files { get; }

   public IReadOnlyList<string> Directories { get; }
}
