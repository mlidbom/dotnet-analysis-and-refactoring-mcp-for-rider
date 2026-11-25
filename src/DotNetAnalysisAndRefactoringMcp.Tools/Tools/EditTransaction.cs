#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.DocumentManagers.Transactions;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Transactions;
using JetBrains.ReSharper.Resources.Shell;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   The PSI transaction an edit tool's refactoring runs in: committed when it ends, or rolled back, as a dry run's is.
///   After a rollback, ReSharper parses the files it changed anew from their documents.
/// </summary>
/// <remarks>
///   A rollback restores the files' text, but the trees it leaves have lost the references of their doc comments: the
///   rename that followed a dry run renamed every use of a type parameter in code, and none of its
///   <c>&lt;typeparamref&gt;</c> tags, however long after the dry run it came (Compze's <c>TeventApplyingPart</c>,
///   2026-11-18).
/// </remarks>
sealed class EditTransaction : IDisposable
{
   readonly IPsiServices _psiServices;
   readonly PsiTransactionCookie _cookie;
   readonly bool _isADryRun;
   IReadOnlyList<IPsiSourceFile>? _changedBeforeTheRollback;

   EditTransaction(IPsiServices psiServices, string name, bool isADryRun)
   {
      _psiServices = psiServices;
      _isADryRun = isADryRun;
      _cookie = isADryRun
                   ? PsiTransactionCookie.CreateTemporaryChangeCookie(psiServices, name)
                   : PsiTransactionCookie.CreateAutoCommitCookieWithCachesUpdate(psiServices, name);
   }

   /// <summary>A transaction named <paramref name="name"/>, which a dry run rolls back when it ends.</summary>
   public static EditTransaction Begin(IPsiServices psiServices, string name, bool dryRun) => new(psiServices, name, dryRun);

   /// <summary>The files whose text the transaction has changed so far.</summary>
   /// <remarks>
   ///   Not every file of its <see cref="IPsiTransaction.AffectedFiles"/>: those count a file for ReSharper's symbol cache
   ///   binding its declarations anew too, which it did in the call after a dry run for every file the dry run had
   ///   rolled back, so that until 2026-11-25 a rename listed the previous rename's files as its own.
   /// </remarks>
   public IReadOnlyList<IPsiSourceFile> ChangedFiles => _changedBeforeTheRollback ?? FilesWhoseTextChanged();

   IReadOnlyList<IPsiSourceFile> FilesWhoseTextChanged()
   {
      var transaction = _psiServices.Transactions.Current ?? throw new InvalidOperationException("No PSI transaction is open.");
      var changedDocuments = _psiServices.Solution.GetComponent<SolutionDocumentTransactionManager>().CurrentTransaction?.GetCurrentRollbackChanges().Keys;
      return [.. transaction.AffectedFiles.Where(it => changedDocuments?.Contains(it.Document) == true)];
   }

   /// <summary>Discards what the transaction changed, now.</summary>
   public void Rollback()
   {
      _changedBeforeTheRollback = ChangedFiles;
      _cookie.Rollback();
   }

   public void Dispose()
   {
      if(_isADryRun) _changedBeforeTheRollback ??= ChangedFiles;
      _cookie.Dispose();
      if(_changedBeforeTheRollback is { } rolledBack) RebuildTheTreesOf(rolledBack);
   }

   void RebuildTheTreesOf(IEnumerable<IPsiSourceFile> files)
   {
      using var writing = WriteLockCookie.Create();
      foreach(var file in files) _psiServices.Files.InvalidatePsiFilesCache(file);
   }
}
