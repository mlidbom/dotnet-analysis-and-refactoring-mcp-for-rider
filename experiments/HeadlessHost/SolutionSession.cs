using System.Diagnostics;
using System.Text.Json;
using JetBrains.Application.FileSystemTracker;
using JetBrains.Application.Processes;
using JetBrains.Application.Threading;
using JetBrains.DocumentManagers.impl;
using JetBrains.DocumentModel;
using JetBrains.DocumentModel.Storage;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Daemon;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Caches;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Feature.Services.CodeCompletion;
using JetBrains.ReSharper.Feature.Services.CodeCompletion.Infrastructure;
using JetBrains.TextControl;
using JetBrains.Threading;
using JetBrains.Util;

namespace HeadlessHost;

/// <summary>Queries and measures one open <see cref="ISolution"/> on the ReSharper main thread.</summary>
sealed class SolutionSession(ISolution solution, IShellLocks locks, Lifetime lifetime)
{
   readonly IFileSystemTracker _tracker = solution.GetComponent<IFileSystemTracker>();
   readonly UsageSearch _search = new(solution);

   public void MeasureWhenIndexed()
   {
      var caches = solution.GetComponent<IPsiCachesState>();
      JetDispatcher.Run(() => !caches.IsIdle.Value, TimeSpan.FromMinutes(3), bThrowOnTimeout: true);
      using var process = Process.GetCurrentProcess();
      var started = new DateTimeOffset(process.StartTime);
      var elapsed = DateTimeOffset.Now - started;
      Console.WriteLine($"INDEXED pid={process.Id} started={started:O} seconds={elapsed.TotalSeconds:F3} " +
                        $"privateBytes={process.PrivateMemorySize64} workingSetBytes={process.WorkingSet64}");
      locks.ReentrancyGuard.Execute("Count projects", () =>
      {
         using var read = locks.UsingReadLock();
         var projects = solution.GetAllProjects().Count(it => !it.ProjectFileLocation.IsNullOrEmpty());
         Console.WriteLine($"PROJECTS {projects} tracker={_tracker.GetType().Name} enabled={_tracker.Enabled} autoCommit={_tracker.AutoCommitAllowed.Value}");
      });
   }

   public bool FindUsages(string symbol) => locks.ReentrancyGuard.Execute("Find usages", symbol, query =>
   {
      using var read = locks.UsingReadLock();
      return _search.Print(query);
   });

   /// <summary>Keeps the dispatcher running between input lines so queued file changes have a chance to arrive.</summary>
   public void Listen()
   {
      while(true)
      {
         Console.WriteLine("READY");
         var input = Task.Run(Console.ReadLine);
         JetDispatcher.Run(input, TimeSpan.MaxValue, bThrowOnTimeout: true);
         if(input.Result is null or "exit") return;
         if(input.Result == "refresh")
            locks.ReentrancyGuard.Execute("Commit file system changes", () => _tracker.CommitChanges());
         else if(input.Result == "wait-swea")
            WaitForSolutionWideAnalysis();
         else if(input.Result.StartsWith("completion-context ", StringComparison.Ordinal))
         {
            var position = input.Result["completion-context ".Length..].Split(' ', 2);
            InspectCompletionContext(position[1], int.Parse(position[0]));
         }
         else if(input.Result == "services")
            locks.ReentrancyGuard.Execute("Read editing services", () =>
            {
               using var read = locks.UsingReadLock();
               Console.WriteLine("SERVICES " + JsonSerializer.Serialize(new {
                  daemon = solution.GetComponent<IDaemon>().GetType().FullName,
                  daemonThread = solution.GetComponent<IDaemonThread>().GetType().FullName,
                  textControls = solution.GetComponent<ITextControlManager>().GetType().FullName,
                  processStartInfoPatcher = solution.GetComponent<IProcessStartInfoPatcher>().GetType().FullName
               }));
            });
         else if(input.Result == "refactorings")
            locks.ReentrancyGuard.Execute("Read C# refactoring support", () =>
            {
               using var read = locks.UsingReadLock();
               CSharpRefactoringSupport.Print(solution);
            });
         else if(input.Result.StartsWith("document ", StringComparison.Ordinal))
            PrintDocument(input.Result["document ".Length..]);
         else if(input.Result.StartsWith("trace ", StringComparison.Ordinal))
            PrintDocument(input.Result["trace ".Length..], trace: true);
         else
            FindUsages(input.Result);
      }
   }

   /// <summary>Checks the document, actual caret offset and applicable contexts used by basic completion.</summary>
   void InspectCompletionContext(string path, int offset) => locks.ReentrancyGuard.Execute("Inspect completion context", () =>
   {
      using var read = locks.UsingReadLock();
      locks.ExecuteWithWriteLock(() => Lifetime.Using(controlLifetime =>
      {
         var document = DocumentOf(ProjectFiles(path)) ?? throw new InvalidOperationException("No project document for " + path);
         var control = solution.GetComponent<ITextControlManager>().CreateTextControl(controlLifetime, document);
         control.Caret.MoveTo(offset, CaretVisualPlacement.DontScrollIfVisible);
         var manager = solution.GetComponent<IntellisenseManager>();
         var parameters = new CodeCompletionParameters(CodeCompletionType.BasicCompletion);
         var contexts = manager.SpecificCodeCompletionContexts(parameters, control);
         var providers = contexts.SelectMany(context => LanguageManager.Instance.GetServices<ICodeCompletionItemsProvider>(context.Language))
                                 .Where(it => (it.SupportedEvaluationMode & parameters.EvaluationMode) != 0)
                                 .Select(it => it.GetType().FullName).Distinct().Order().ToArray();
         var result = manager.GetCompletionResult(parameters, control);
         Console.WriteLine("COMPLETION-CONTEXT " + JsonSerializer.Serialize(new {
            control = control.GetType().FullName, sameDocument = control.Document == document,
            requestedOffset = offset, actualOffset = (int)control.Caret.Position.Value.ToDocOffset(),
            contexts = contexts.Select(it => it.GetType().FullName).ToArray(), providers,
            result = result?.GetType().FullName, itemCount = result?.LookupItems?.Count
         }));
      }));
   });

   /// <summary>Allows solution-wide analysis to catch up before comparing daemon inspection results.</summary>
   void WaitForSolutionWideAnalysis()
   {
      var analysis = SolutionAnalysisConfiguration.GetInstance(solution);
      var timer = Stopwatch.StartNew();
      JetDispatcher.Run(() => analysis.Enabled.Value && !analysis.Completed.Value,
                        TimeSpan.FromSeconds(15), bThrowOnTimeout: false);
      Console.WriteLine("SWEA " + JsonSerializer.Serialize(new {
         enabled = analysis.Enabled.Value, completed = analysis.Completed.Value, elapsedMs = timer.ElapsedMilliseconds
      }));
   }

   /// <summary>Reads a project document directly, without Royder's pre-call disk synchronization.</summary>
   void PrintDocument(string path, bool trace = false) => locks.ReentrancyGuard.Execute("Read experiment document", () =>
   {
      using var read = locks.UsingReadLock();
      var items = ProjectFiles(path);
      var document = DocumentOf(items);
      var storage = solution.GetComponent<IDocumentStorageHelpers>();
      if(trace && document != null) TraceDocument(document, (DocumentOperationsImpl)storage);
      Console.WriteLine("DOCUMENT " + JsonSerializer.Serialize(new {
         projectItems = items.Length, text = document?.GetText(), storage = storage.GetType().FullName,
         unsaved = document != null && ((DocumentOperationsImpl)storage).IsDocumentUnsaved(document)
      }));
   });

   IProjectFile[] ProjectFiles(string path) =>
      solution.FindProjectItemsByLocation(VirtualFileSystemPath.Parse(path, InteractionContext.SolutionContext))
              .OfType<IProjectFile>().ToArray();

   static IDocument? DocumentOf(IProjectFile[] items) =>
      items.Select(it => it.ToSourceFiles().FirstOrDefault()).FirstOrDefault(it => it != null)?.Document;

   /// <summary>Records who first changes the document and who saves it in this session.</summary>
   void TraceDocument(IDocument document, DocumentOperationsImpl storage)
   {
      document.DocumentChanged += OnChanged;
      storage.AfterDocumentSaved.Advise(lifetime, saved =>
      {
         if(saved.Document == document)
            Console.WriteLine("DOCUMENT-SAVE " + JsonSerializer.Serialize(Environment.StackTrace));
      });

      void OnChanged(object sender, JetBrains.DataFlow.EventArgs<DocumentChange> change)
      {
         document.DocumentChanged -= OnChanged;
         Console.WriteLine("DOCUMENT-CHANGE " + JsonSerializer.Serialize(Environment.StackTrace));
      }
   }
}
