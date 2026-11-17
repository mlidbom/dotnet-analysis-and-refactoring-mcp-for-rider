#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Application.Progress;
using JetBrains.Application.Threading.Tasks;
using JetBrains.DocumentModel;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Daemon;
using JetBrains.ReSharper.Daemon.SolutionAnalysis;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Psi;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   inspect_code: ReSharper's inspections over files, folders, projects or the solution, run by the engine of Rider's
///   Inspect Code (<see cref="CollectInspectionResults"/>), in parallel and against Rider's live model: what
///   <c>jb inspectcode</c> does headless, without loading the solution again.
/// </summary>
/// <remarks>
///   <para>
///     The engine runs the files on worker threads, under read locks of its own, after committing documents, and
///     terminates the lifetime it is given when it's done. It's started on the main thread, as Rider's action starts it.
///   </para>
///   <para>
///     With solution-wide analysis on and complete, <see cref="CollectInspectionResults.RunGlobalInspections"/> takes
///     what that analysis found, and runs the global usage inspections (UnusedMember.Global and the like) besides. Its
///     task executor runs on the caller's thread, and with the analysis complete it only reads its results. Otherwise
///     <see cref="CollectInspectionResults.RunLocalInspections"/> runs the file daemon on every file.
///   </para>
///   <para>
///     Measured on Deskmancer, 1,749 files, in the sandbox (2026-11-15): 5.5 s with solution-wide analysis complete,
///     1,111 issues at warning; the main thread's watchdog reported nothing. Right after the solution opened, with that
///     analysis still running, the file daemon got through 1,140 files in the 100 s.
///   </para>
/// </remarks>
public sealed class InspectCodeTool : IMcpAsyncTool, IMcpExpandableTool
{
   /// <summary>Within the host's 120 seconds per call, so that what was inspected by then is answered.</summary>
   static readonly TimeSpan HowLongInspectingMayTake = TimeSpan.FromSeconds(100);

   const int DefaultMaxResults = 200;

   readonly ISolution _solution;

   public InspectCodeTool(ISolution solution) => _solution = solution;

   public string Name => "inspect_code";

   public string Description =>
      "Run ReSharper's inspections over files, folders, projects or the whole solution, as Rider's Inspect Code does, " +
      "in parallel and against Rider's live model: the whole-solution sweep without jb inspectcode. Pass files, folders " +
      "or project files in 'filePaths' (or one in 'filePath'); none inspects the solution. Pass 'changedSince' (a git " +
      "ref) or 'changed: true' to inspect every source file changed since then, committed or not, and untracked ones. " +
      "Filter with minSeverity " +
      "('error'|'warning'|'suggestion'|'hint', default 'warning') and inspectionIds. Prints the count per inspection, " +
      "then rg-style 'line:column severity id: message' lines per file, up to maxResults. Stops after 100 s and answers " +
      "what it inspected by then. Inspections that need solution-wide analysis, such as UnusedMember.Global, are in when " +
      "Rider's solution-wide analysis is on and complete; the answer says. Issues are as Inspect Code reports them: some " +
      "inspections report once per declaration where get_diagnostics reports each occurrence.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   filePath = new { type = "string", description = "A file, folder or project file to inspect." },
                                                   filePaths = new { type = "array", items = new { type = "string" }, description = "Files, folders or project files to inspect. None: the whole solution." },
                                                   changedSince = new { type = "string", description = "Inspect every source file changed since this git ref (committed or not) plus untracked ones, e.g. 'HEAD', 'origin/main'." },
                                                   changed = new { type = "boolean", description = "Shorthand for changedSince: 'HEAD': every modified or untracked source file." },
                                                   minSeverity = new { type = "string", description = "'error', 'warning' (default), 'suggestion' or 'hint'." },
                                                   inspectionIds = new { type = "array", items = new { type = "string" }, description = "Only these inspections, such as 'UnusedVariable'." },
                                                   maxResults = new { type = "integer", description = $"Issue lines to print; the rest are counted. Default {DefaultMaxResults}." }
                                                }
                                };

   object IMcpTool.Execute(JObject arguments) => throw new InvalidOperationException("Use the asynchronous tool dispatcher.");

   public JObject Expand(JObject arguments) => ChangedSinceArgument.Expand(arguments, _solution);

   public async Task<object> ExecuteAsync(JObject arguments, McpToolExecution execution)
   {
      var paths = (arguments["filePaths"] as JArray)?.Select(it => it.ToString()).ToList() ?? [];
      if(arguments["filePath"]?.ToString() is { Length: > 0 } single) paths.Add(single);
      var minSeverity = Severities.Parse(arguments["minSeverity"]?.ToString()) ?? Severity.WARNING;
      var inspectionIds = (arguments["inspectionIds"] as JArray)?.Select(it => it.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
      var maxResults = arguments["maxResults"]?.Value<int>() ?? DefaultMaxResults;

      // No changed files is no files, not the whole solution, which no paths would otherwise mean.
      var changedScope = ChangedSinceArgument.ScopeOf(arguments);
      if(changedScope != null && ChangedSinceArgument.ErrorOf(arguments) is { } changedError)
         return new { error = $"Cannot list files {changedScope}: {changedError}" };
      if(changedScope != null && paths.Count == 0)
         return $"{changedScope}: no changed source files";
      var scopeLabel = changedScope == null ? "" : changedScope + ": ";

      var scope = await execution.Read(() => FilesIn(paths)).ConfigureAwait(false);
      if(scope.Files.Count == 0)
         return new { error = scopeLabel + (scope.NotFound.Count > 0 ? "Nothing to inspect in " + Listed(scope.NotFound) : "The solution has no files to inspect.") };

      var issues = new ConcurrentBag<Issue>();
      var inspected = 0;
      var stopwatch = Stopwatch.StartNew();

      using var call = Lifetime.Define(Lifetime.Eternal, "DotNetAnalysisAndRefactoringMcp.inspect_code");
      var inspection = call.Lifetime.CreateNested();
      var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
      inspection.Lifetime.OnTermination(() => done.TrySetResult(true));

      void Keep(IPsiSourceFile? file, IssuePointer issue)
      {
         if(file != null && Issue.Of(file, issue) is {} kept && Severities.AtLeast(kept.Severity, minSeverity)
         && (inspectionIds == null || inspectionIds.Contains(kept.InspectionId)))
            issues.Add(kept);
      }

      var solutionWide = SolutionAnalysisConfiguration.GetInstance(_solution);
      var global = solutionWide.Enabled.Value && solutionWide.Completed.Value;

      await _solution.Locks.Tasks.StartNew(call.Lifetime,
                                           Scheduling.MainGuard,
                                           () =>
                                           {
                                              var engine = new CollectInspectionResults(_solution, inspection, new ProgressIndicator(inspection.Lifetime));
                                              var files = new Stack<IPsiSourceFile>(scope.Files);
                                              if(global)
                                                 engine.RunGlobalInspections(SimpleTaskExecutor.Instance,
                                                                             files,
                                                                             found =>
                                                                             {
                                                                                foreach(var issue in found) Keep(issue.File.File, issue);
                                                                             });
                                              else
                                                 engine.RunLocalInspections(files,
                                                                            (file, found) =>
                                                                            {
                                                                               foreach(var issue in found) Keep(file, issue);
                                                                               System.Threading.Interlocked.Increment(ref inspected);
                                                                            });
                                           })
                     .ConfigureAwait(false);

      var finished = await Task.WhenAny(done.Task, Task.Delay(HowLongInspectingMayTake, call.Lifetime)).ConfigureAwait(false) == done.Task;
      if(!finished) inspection.Terminate();

      return scopeLabel + Answer(issues.ToList(),
                    scope,
                    global ? null : inspected,
                    finished,
                    minSeverity,
                    maxResults,
                    stopwatch.Elapsed,
                    global ? null : solutionWide.Enabled.Value ? "is still running" : "is off");
   }

   /// <param name="inspected">How many files were inspected, when they were inspected one by one.</param>
   /// <param name="solutionWideAnalysis">Why the inspections that need solution-wide analysis weren't run; nothing if they were.</param>
   string Answer(List<Issue> issues,
                 Scope scope,
                 int? inspected,
                 bool finished,
                 Severity minSeverity,
                 int maxResults,
                 TimeSpan took,
                 string? solutionWideAnalysis)
   {
      var root = SolutionRoot.Of(_solution);
      var answer = new StringBuilder();
      var files = issues.Select(it => it.File).Distinct().Count();
      answer.Append($"{issues.Count} issues at {Severities.Name(minSeverity)} or above in {files} files; ");
      answer.AppendLine(finished
                           ? $"inspected {scope.Files.Count} files in {Seconds(took)}."
                           : inspected is {} count
                              ? $"stopped after {Seconds(took)}, having inspected {count} of {scope.Files.Count} files: narrow the scope for the rest."
                              : $"stopped after {Seconds(took)}, before every file was inspected: narrow the scope for the rest.");
      answer.AppendLine(solutionWideAnalysis == null
                           ? "With solution-wide analysis: its inspections, such as UnusedMember.Global, are in."
                           : $"Solution-wide analysis {solutionWideAnalysis}, so its inspections, such as UnusedMember.Global, are left out.");
      if(scope.NotFound.Count > 0)
         answer.AppendLine("Nothing to inspect in " + Listed(scope.NotFound));
      if(issues.Count == 0) return answer.ToString().TrimEnd();

      answer.AppendLine("by inspection:");
      foreach(var group in issues.GroupBy(it => (it.InspectionId, it.Severity)).OrderByDescending(it => it.Count()).ThenBy(it => it.Key.InspectionId, StringComparer.Ordinal))
         answer.AppendLine($"  {group.Count(),5} {Severities.Name(group.Key.Severity)} {group.Key.InspectionId}");

      var printed = 0;
      foreach(var file in issues.GroupBy(it => it.File).OrderBy(it => it.Key, StringComparer.OrdinalIgnoreCase))
      {
         if(printed >= maxResults) break;
         answer.AppendLine(root.Relative(file.Key));
         foreach(var issue in file.OrderBy(it => it.Line).ThenBy(it => it.Column))
         {
            if(printed++ >= maxResults) break;
            answer.AppendLine($"  {issue.Line}:{issue.Column} {Severities.Name(issue.Severity)} {issue.InspectionId}: {issue.Message}");
         }
      }

      if(issues.Count > maxResults)
         answer.AppendLine($"({issues.Count - maxResults} more issues not printed: raise maxResults, or narrow with filePaths or inspectionIds)");
      return answer.ToString().TrimEnd();
   }

   /// <summary>The first few of <paramref name="paths"/>, and how many more: changed files in no project of the solution can be many.</summary>
   static string Listed(IReadOnlyList<string> paths) =>
      string.Join(", ", paths.Take(5)) + (paths.Count > 5 ? $", +{paths.Count - 5} more" : "");

   static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

   /// <summary>An issue as plain values, taken while the engine holds its read lock: nothing of the engine's is kept.</summary>
   sealed class Issue
   {
      public string File { get; private set; } = "";
      public int Line { get; private set; }
      public int Column { get; private set; }
      public Severity Severity { get; private set; }
      public string InspectionId { get; private set; } = "";
      public string Message { get; private set; } = "";

      public static Issue? Of(IPsiSourceFile file, IssuePointer issue)
      {
         if(!issue.IsValid) return null;
         var (line, column) = PsiHelpers.GetLineColumn(new DocumentOffset(file.Document, issue.NavigationOffset));
         return new Issue
                {
                   File = file.GetLocation().FullPath,
                   Line = line,
                   Column = column,
                   Severity = issue.GetSeverity(),
                   InspectionId = issue.IssueType.ConfigurableSeverityId ?? issue.IssueType.GroupId,
                   Message = issue.Message
                };
      }
   }

   sealed class Scope(List<IPsiSourceFile> files, List<string> notFound)
   {
      public List<IPsiSourceFile> Files { get; } = files;
      public List<string> NotFound { get; } = notFound;
   }

   /// <summary>
   ///   The source files of the projects, under the folders, and the files the paths name; every project's when there
   ///   are no paths. A project file stands for its project's files.
   /// </summary>
   Scope FilesIn(IReadOnlyCollection<string> paths)
   {
      var all = _solution.GetAllProjects()
                         .Where(it => !it.ProjectFileLocation.IsEmpty)
                         .SelectMany(project => project.GetAllProjectFiles().Select(file => (Project: project, Source: file.ToSourceFile())))
                         .Where(it => it.Source != null && it.Source.IsValid())
                         .GroupBy(it => it.Source!.GetLocation().FullPath, StringComparer.OrdinalIgnoreCase)
                         .Select(it => it.First())
                         .ToList();
      if(paths.Count == 0) return new Scope([.. all.Select(it => it.Source!)], []);

      var root = SolutionRoot.Of(_solution);
      var files = new Dictionary<string, IPsiSourceFile>(StringComparer.OrdinalIgnoreCase);
      var notFound = new List<string>();
      foreach(var path in paths)
      {
         var full = Path.GetFullPath(root.Absolute(SolutionRoot.Trimmed(path)) ?? path);
         var matches = all.Where(it => string.Equals(it.Project.ProjectFileLocation.FullPath, full, StringComparison.OrdinalIgnoreCase)
                                    || IsInOrIs(it.Source!.GetLocation().FullPath, full))
                          .ToList();
         if(matches.Count == 0) notFound.Add(path);
         foreach(var match in matches) files[match.Source!.GetLocation().FullPath] = match.Source!;
      }

      return new Scope([.. files.Values], notFound);
   }

   static bool IsInOrIs(string path, string folderOrFile) =>
      string.Equals(path, folderOrFile, StringComparison.OrdinalIgnoreCase)
   || path.StartsWith(folderOrFile.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
