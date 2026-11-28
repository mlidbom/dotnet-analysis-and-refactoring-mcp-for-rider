#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Application.Threading;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   ReSharper's caches of files rebuilt from their documents, the way ReSharper's own <c>PsiCachesRepairService</c>
///   rebuilds them when it finds them inconsistent with a file: the file's syntax trees and every cache of it marked
///   dirty, then all documents committed.
/// </summary>
/// <remarks>
///   <para>
///     For files changed on disk whose change Rider's project model hasn't registered yet. ReSharper counts its caches
///     of a file current for as long as the project model's last write time of it is unchanged: the symbols of its
///     declarations, and the trees it rebuilds the file's syntax tree from. A document created after the change reads
///     the disk's text, so the disk sync finds it current, while those caches still describe the text before the
///     change, at its offsets.
///   </para>
///   <para>
///     The frontend registers a change when asked to rescan the file, and otherwise at its next refresh: every 15 s in
///     the standalone primary's private Rider (2026-11-28). Before then <c>get_file_errors</c> answered from those
///     caches, 2.3 s after a change: "{ expected" where a method's body had begun before a comment above it grew, and
///     "Cannot resolve symbol" for a member of a class a grown comment had moved. ReSharper repaired its caches of the
///     files only after the answer (<c>PsiCachesRepairService</c> in the backend's log).
///   </para>
/// </remarks>
sealed class CacheRepairs(ISolution solution, IShellLocks locks)
{
   static readonly ILogger Log = Logger.GetLogger<CacheRepairs>();

   /// <summary>Rebuilds ReSharper's caches of <paramref name="files"/>, on the main thread under the write lock.</summary>
   public async Task Repair(Lifetime lifetime, IReadOnlyCollection<string> files)
   {
      await locks.ExecuteOrQueueReadLockAsync(lifetime,
                                              "DotNetAnalysisAndRefactoringMcp.CacheRepairs",
                                              () => locks.ExecuteWithWriteLock(() =>
                                              {
                                                 var psi = solution.GetPsiServices();
                                                 foreach(var sourceFile in files.SelectMany(SourceFilesAt).Where(it => it.IsValid()))
                                                 {
                                                    psi.Files.MarkAsDirty(sourceFile);
                                                    psi.Caches.MarkAsDirty(sourceFile);
                                                 }

                                                 psi.Files.CommitAllDocuments();
                                              })).ConfigureAwait(false);

      Log.Verbose($"Rebuilt ReSharper's caches of {files.Count} files whose change on disk the project model hadn't registered: {string.Join(", ", files)}");
   }

   IEnumerable<IPsiSourceFile> SourceFilesAt(string file) =>
      solution.FindProjectItemsByLocation(VirtualFileSystemPath.Parse(file, InteractionContext.SolutionContext))
              .OfType<IProjectFile>()
              .SelectMany(it => it.ToSourceFiles().ToList());
}
