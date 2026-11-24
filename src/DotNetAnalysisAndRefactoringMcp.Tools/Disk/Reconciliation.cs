#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using JetBrains.Application.Threading;
using JetBrains.DocumentManagers.impl;
using JetBrains.DocumentModel;
using JetBrains.DocumentModel.Storage;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   Rider's copy of documents brought back in line with their files on disk, through Rider's own
///   <c>SynchronizeDocumentContentsWithStorage</c>.
/// </summary>
/// <remarks>
///   For a document no editor owns, Rider applies the disk's text as an external change and marks the file reconciled;
///   for one an editor owns, it leaves the text to the frontend. A document with unsaved edits is left alone. Never
///   edit a document's text directly instead: that is an in-memory edit, the document counts as unsaved, and autosave
///   later writes it over whatever is on disk by then (observed with <c>DocumentBindingUtil.SyncDocumentsWithFiles</c>).
/// </remarks>
sealed class Reconciliation(ISolution solution, IShellLocks locks)
{
   static readonly ILogger Log = Logger.GetLogger<Reconciliation>();

   /// <summary>
   ///   Reconciles <paramref name="documents"/> with disk on the main thread under the write lock. They are taken, and
   ///   whether each has unsaved edits is asked, there, so an edit made since the caller looked is never overwritten.
   /// </summary>
   public async Task Reconcile(Lifetime lifetime, Func<IEnumerable<IDocument>> documents)
   {
      try
      {
         await locks.ExecuteOrQueueReadLockAsync(lifetime,
                                                 "DotNetAnalysisAndRefactoringMcp.Reconciliation",
                                                 () =>
                                                 {
                                                    locks.ExecuteWithWriteLock(() =>
                                                    {
                                                       if(solution.TryGetComponent<IDocumentStorageHelpers>() is not DocumentOperationsImpl operations)
                                                          return;

                                                       foreach(var document in documents())
                                                          if(File.Exists(document.Moniker) && !operations.IsDocumentUnsaved(document))
                                                             operations.SynchronizeDocumentContentsWithStorage(document, VirtualFileSystemPath.Parse(document.Moniker, InteractionContext.SolutionContext));

                                                       solution.GetPsiServices().Files.CommitAllDocuments();
                                                    });
                                                 }).ConfigureAwait(false);
      }
      catch(Exception exception) when(exception is not OperationCanceledException)
      {
         Log.Warn(exception, ExceptionOrigin.Algorithmic, "DotNetAnalysisAndRefactoringMcp could not reconcile changed files with disk");
      }
   }
}
