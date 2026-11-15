using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Application.Threading;
using JetBrains.DocumentManagers.impl;
using JetBrains.DocumentModel;
using JetBrains.DocumentModel.Storage;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;

namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   The documents of a solution whose text differs from their files on disk until Rider saves them, such as those an
///   edit tool changed: Rider saves those a while later, so a build started right after the edit would build the old
///   text.
/// </summary>
/// <remarks>
///   <para>
///     Rider's frontend owns a document's text and saves it: Rider's backend saves a document by asking the frontend
///     to (<c>RiderDocumentOperationsImpl.SaveDocument</c>, through the protocol's <c>SaveFiles</c>), and it is saved
///     once the frontend has written it. Its <c>SaveDocumentAsync</c> waits for that, and could replace the waiting
///     here since the plugin builds against the 2026.3 SDK, which has it. Not tried yet.
///   </para>
///   <para>
///     Unsaved means that the text on disk differs, by <see cref="DiskText"/>. Rider's own <c>IsDocumentUnsaved</c>
///     still said so of a document 10 s after the frontend had written it (2026-11-15), so it only picks the documents
///     to compare.
///   </para>
/// </remarks>
sealed class UnsavedDocuments
{
   static readonly TimeSpan HowLongSavingMayTake = TimeSpan.FromSeconds(10);

   /// <summary>
   ///   How long Rider's own save of what a refactoring changed may take, which Rider sends right after its transaction,
   ///   before the call saves what it left. Rider's own landed within 50 ms, in the logs of 2026-11-15.
   /// </summary>
   /// <remarks>
   ///   A second save, sent 2 ms after Rider's own, had the file written twice in a row, and Rider, reloading it from disk
   ///   meanwhile, was left with an empty copy of it, in 2 of 50 moves (2026-11-15). The empty copy stayed until the next
   ///   call's disk sync reloaded it, and the call waited <see cref="HowLongSavingMayTake"/> for nothing.
   /// </remarks>
   static readonly TimeSpan HowLongRidersOwnSaveMayTake = TimeSpan.FromMilliseconds(500);
   static readonly TimeSpan BetweenChecks = TimeSpan.FromMilliseconds(50);

   readonly DocumentOperationsImpl _operations;
   readonly IShellLocks _locks;

   public UnsavedDocuments(ISolution solution)
   {
      _operations = (DocumentOperationsImpl)solution.GetComponent<IDocumentStorageHelpers>();
      _locks = solution.Locks;
   }

   /// <summary>Those unsaved now: before an edit, the ones its saving leaves alone.</summary>
   public async Task<List<IDocument>> Now(Lifetime lifetime) =>
      [.. (await UnsavedOf(() => _operations.GetUnsavedDocuments(), lifetime).ConfigureAwait(false)).Select(it => it.Document)];

   /// <summary>
   ///   Saves the documents unsaved now that weren't <paramref name="before"/>, and returns once Rider's frontend has saved
   ///   them, or after <see cref="HowLongSavingMayTake"/> with those it hadn't, and how their files differed. What Rider
   ///   saves by itself within <see cref="HowLongRidersOwnSaveMayTake"/> isn't saved again.
   /// </summary>
   /// <remarks>
   ///   Not those unsaved before: their unsaved text is someone's work in progress, such as Magnus's typing in Rider,
   ///   which is his to save.
   /// </remarks>
   public async Task<List<DocumentNotSaved>> SaveThoseUnsavedSince(List<IDocument> before, Lifetime lifetime)
   {
      var changed = (await Now(lifetime).ConfigureAwait(false)).Except(before).Where(HasItsFile).ToList();
      var leftByRider = await UnsavedAfter(changed, HowLongRidersOwnSaveMayTake, lifetime).ConfigureAwait(false);
      if(leftByRider.Count == 0)
         return [];

      await OnTheMainThread(lifetime,
                            () =>
                            {
                               foreach(var (document, _) in leftByRider)
                                  _operations.SaveDocument(document);
                               return leftByRider;
                            }).ConfigureAwait(false);

      var unsaved = await UnsavedAfter([.. leftByRider.Select(it => it.Document)], HowLongSavingMayTake, lifetime).ConfigureAwait(false);
      return [.. unsaved.Select(it => new DocumentNotSaved(it.Document.Moniker, DiskText.DifferenceOf(it.Document.Moniker, it.Text)))];
   }

   /// <summary>
   ///   Those of <paramref name="documents"/> whose files don't hold their text once they all do, or once
   ///   <paramref name="howLong"/> has passed.
   /// </summary>
   async Task<List<(IDocument Document, string Text)>> UnsavedAfter(List<IDocument> documents, TimeSpan howLong, Lifetime lifetime)
   {
      var deadline = DateTime.UtcNow + howLong;
      while(true)
      {
         var unsaved = await UnsavedOf(() => documents, lifetime).ConfigureAwait(false);
         if(unsaved.Count == 0 || DateTime.UtcNow >= deadline)
            return unsaved;
         await Task.Delay(BetweenChecks, lifetime).ConfigureAwait(false);
      }
   }

   /// <summary>
   ///   Whether <paramref name="document"/>'s file is on disk. One whose file isn't, and that was saved before the edit,
   ///   is one whose file the edit renamed, such as a renamed type's: the document of the file's new name holds its text.
   /// </summary>
   /// <remarks>
   ///   Saving the old one waited <see cref="HowLongSavingMayTake"/> for a file that wasn't coming, and then said that Rider
   ///   hadn't saved it (2026-11-15).
   /// </remarks>
   static bool HasItsFile(IDocument document) => File.Exists(document.Moniker);

   /// <summary>
   ///   Those of <paramref name="documents"/> whose files on disk don't hold their text, with that text: the texts are
   ///   taken on the main thread, the files read off it.
   /// </summary>
   async Task<List<(IDocument Document, string Text)>> UnsavedOf(Func<IEnumerable<IDocument>> documents, Lifetime lifetime)
   {
      var texts = await OnTheMainThread(lifetime, () => documents().Select(it => (Document: it, Text: it.GetText())).ToList()).ConfigureAwait(false);
      return [.. texts.Where(it => !DiskText.Holds(it.Document.Moniker, it.Text))];
   }

   /// <summary>Under the read lock on the main thread, where Rider saves documents.</summary>
   Task<T> OnTheMainThread<T>(Lifetime lifetime, Func<T> read) =>
      _locks.ExecuteOrQueueReadLockAsync(lifetime, "DotNetAnalysisAndRefactoringMcp.UnsavedDocuments", read);
}
