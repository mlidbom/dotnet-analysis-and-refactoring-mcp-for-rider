#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DotNetAnalysisAndRefactoringMcp.Threading;
using JetBrains.Application.Components;
using JetBrains.Application.Threading.Tasks;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ProjectModel.ProjectsHost;
using JetBrains.ProjectModel.ProjectsHost.SolutionHost;
using JetBrains.ProjectModel.ProjectsHost.SolutionHost.Impl;
using JetBrains.ReSharper.Psi.Caches;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   Rider's Reload Project, for projects whose model in Rider is stale, and a wait until the reload is done: Rider has
///   loaded them, applied the result, and ReSharper has indexed what changed. Then the tools' answers are current.
/// </summary>
/// <remarks>
///   <para>
///     Rider's own action ends in <see cref="ISolutionHost.ReloadProjectsAsync"/>, on the main thread. The load runs in
///     the background, between <c>LoadEvents.Start</c> and <c>LoadEvents.Finish</c>, and Rider's executor then queues
///     the result's application to the main thread. So after <c>Finish</c>, two turns on the main thread come after it,
///     and then the caches are waited for.
///   </para>
///   <para>
///     Replaces <c>~/.claude/tools/rider_reload_projects.py</c>, which inserts a comment into a project's
///     <c>nuget.g.props</c> and has the rider MCP lint it, to make Rider's NuGet tracker reload the project.
///   </para>
/// </remarks>
public sealed class ReloadProjectsTool : IMcpAsyncTool
{
   static readonly TimeSpan HowLongLoadingMayTake = TimeSpan.FromSeconds(90);
   static readonly TimeSpan HowLongIndexingMayTake = TimeSpan.FromSeconds(20);
   static readonly TimeSpan BetweenIndexingChecks = TimeSpan.FromMilliseconds(250);

   readonly ISolution _solution;

   public ReloadProjectsTool(ISolution solution) => _solution = solution;

   public string Name => "reload_projects";

   public string Description =>
      "Reload projects in Rider, as its Reload Project does, and return once Rider has loaded them and ReSharper has " +
      "indexed the result. For a project whose model in Rider is stale: errors that the build doesn't have, such as " +
      "'Cannot resolve symbol InitializeComponent' in a new view's code-behind. Pass project files, or any file or folder " +
      "in the projects: a file's project is the one whose project file is in the nearest folder above it.";

   public object InputSchema => new
                                {
                                   type = "object",
                                   properties = new
                                                {
                                                   filePath = new { type = "string", description = "A project file, or a file or folder in the project to reload." },
                                                   filePaths = new { type = "array", items = new { type = "string" }, description = "Several, for several projects." }
                                                }
                                };

   object IMcpTool.Execute(JObject arguments) => throw new InvalidOperationException("Use the asynchronous tool dispatcher.");

   public async Task<object> ExecuteAsync(JObject arguments, McpToolExecution execution)
   {
      var paths = (arguments["filePaths"] as JArray)?.Select(it => it.ToString()).ToList() ?? [];
      if(arguments["filePath"]?.ToString() is { Length: > 0 } single) paths.Add(single);
      if(paths.Count == 0)
         return new { error = "Pass 'filePath' or 'filePaths': project files, or files or folders in the projects to reload." };

      var found = await execution.Read(() => ProjectsOf(paths)).ConfigureAwait(false);
      if(found.Projects.Count == 0)
         return new { error = "No project found for " + string.Join(", ", found.NotFound) };

      var solutionHost = _solution.ProjectsHostContainer().GetComponent<ISolutionHost>();
      var loadEvents = (solutionHost as SolutionHost)?.LoadEvents;

      using var call = Lifetime.Define(Lifetime.Eternal, "DotNetAnalysisAndRefactoringMcp.reload_projects");
      var load = new ProjectsLoad(found.Projects.Select(it => it.Mark));
      if(loadEvents != null)
      {
         loadEvents.Start.Advise(call.Lifetime, load.Started);
         loadEvents.Finish.Advise(call.Lifetime, load.Finished);
      }

      var stopwatch = Stopwatch.StartNew();
      await _solution.Locks.Tasks.StartNew(call.Lifetime,
                                           Scheduling.MainGuard,
                                           () => solutionHost.ReloadProjectsAsync([.. found.Projects.Select(it => it.Mark)], null))
                     .ConfigureAwait(false);

      var answer = new StringBuilder();
      if(loadEvents == null)
      {
         answer.AppendLine("Asked Rider to reload them, but this Rider's solution host doesn't report loads, so the reload wasn't waited for.");
      } else if(await Task.WhenAny(load.Done, Task.Delay(HowLongLoadingMayTake, call.Lifetime)).ConfigureAwait(false) != load.Done)
      {
         answer.AppendLine($"Asked Rider to reload them, and it hadn't loaded them after {Seconds(HowLongLoadingMayTake)}. " +
                           "Rider holds reloads back during a build, for example.");
      } else
      {
         // The result is applied on the main thread, queued when the load finished: these turns come after it.
         await MainThreadTurn(call.Lifetime).ConfigureAwait(false);
         await MainThreadTurn(call.Lifetime).ConfigureAwait(false);
         var loadedIn = stopwatch.Elapsed;
         var indexed = await IndexingIsDone(call.Lifetime).ConfigureAwait(false);
         answer.AppendLine(indexed
                              ? $"Reloaded in {Seconds(loadedIn)}, and ReSharper has indexed the result ({Seconds(stopwatch.Elapsed)} in all)."
                              : $"Reloaded in {Seconds(loadedIn)}; ReSharper was still indexing {Seconds(HowLongIndexingMayTake)} later, so answers may be incomplete for a while.");
      }

      foreach(var project in found.Projects)
         answer.AppendLine("  " + project.Name + " — " + project.File);
      if(found.NotFound.Count > 0)
         answer.AppendLine("No project found for " + string.Join(", ", found.NotFound));
      return answer.ToString().TrimEnd();
   }

   static string Seconds(TimeSpan time) => time.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

   Task MainThreadTurn(Lifetime lifetime) => _solution.Locks.Tasks.StartNew(lifetime, Scheduling.MainGuard, () => {});

   /// <summary>Whether the caches went idle and stayed so between two checks, within <see cref="HowLongIndexingMayTake"/>.</summary>
   async Task<bool> IndexingIsDone(Lifetime lifetime)
   {
      var caches = _solution.TryGetComponent<IPsiCachesState>();
      if(caches == null) return true;
      var deadline = DateTime.UtcNow + HowLongIndexingMayTake;
      var idleBefore = false;
      while(DateTime.UtcNow < deadline)
      {
         var idle = caches.IsIdle.Value;
         if(idle && idleBefore) return true;
         idleBefore = idle;
         await Task.Delay(BetweenIndexingChecks, lifetime).ConfigureAwait(false);
      }

      return false;
   }

   sealed class FoundProject(string name, string file, IProjectMark mark)
   {
      public string Name { get; } = name;
      public string File { get; } = file;
      public IProjectMark Mark { get; } = mark;
   }

   sealed class Found(List<FoundProject> projects, List<string> notFound)
   {
      public List<FoundProject> Projects { get; } = projects;
      public List<string> NotFound { get; } = notFound;
   }

   /// <summary>
   ///   Each path's project: the one with that project file, or else the one whose project file is in the nearest folder
   ///   above it, so a file Rider's model doesn't hold yet finds its project too.
   /// </summary>
   Found ProjectsOf(IEnumerable<string> paths)
   {
      var root = SolutionRoot.Of(_solution);
      var candidates = _solution.GetAllProjects()
                                .Where(it => !it.ProjectFileLocation.IsEmpty && it.GetProjectMark() != null)
                                .Select(it => (Project: it, File: it.ProjectFileLocation.FullPath, Folder: it.ProjectFileLocation.Directory.FullPath))
                                .ToList();

      var projects = new List<FoundProject>();
      var notFound = new List<string>();
      foreach(var path in paths)
      {
         var full = Path.GetFullPath(root.Absolute(SolutionRoot.Trimmed(path)) ?? path);
         var match = candidates.FirstOrDefault(it => string.Equals(it.File, full, StringComparison.OrdinalIgnoreCase));
         if(match.Project == null)
            match = candidates.Where(it => IsInOrIs(full, it.Folder)).OrderByDescending(it => it.Folder.Length).FirstOrDefault();

         if(match.Project == null)
            notFound.Add(path);
         else if(projects.All(it => !Equals(it.Mark, match.Project.GetProjectMark())))
            projects.Add(new FoundProject(match.Project.Name, root.Relative(match.File), match.Project.GetProjectMark()!));
      }

      return new Found(projects, notFound);
   }

   static bool IsInOrIs(string path, string folder) =>
      string.Equals(path, folder, StringComparison.OrdinalIgnoreCase)
   || path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

   /// <summary>
   ///   Which of the projects Rider has loaded: a load reports the projects it starts on, and then that it finished.
   ///   A project Rider has unloaded, or filters out, is never started on.
   /// </summary>
   sealed class ProjectsLoad
   {
      readonly IThreadShared<Projects> _projects;
      readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

      public ProjectsLoad(IEnumerable<IProjectMark> projects) => _projects = ThreadShared.New(new Projects([.. projects]));

      public Task Done => _done.Task;

      public void Started(ICollection<IProjectMark> projects)
      {
         _projects.Locked(it =>
         {
            it.Loading.Clear();
            it.Loading.UnionWith(projects.Where(it.WaitingFor.Contains));
         });
      }

      /// <remarks>
      ///   The batch's end, not <c>FinishLoadProject</c>'s per project: the result is queued for applying only once the
      ///   whole batch is loaded.
      /// </remarks>
      /// <param name="_">Nothing: what a <c>SimpleSignal</c> hands its subscribers.</param>
      public void Finished(bool _)
      {
         _projects.Locked(it =>
         {
            it.WaitingFor.ExceptWith(it.Loading);
            it.Loading.Clear();
            if(it.WaitingFor.Count == 0) _done.TrySetResult(true);
         });
      }

      /// <summary>The projects not loaded yet, and those the load in progress started on: what Rider's load signals share.</summary>
      sealed class Projects(HashSet<IProjectMark> waitingFor)
      {
         public readonly HashSet<IProjectMark> WaitingFor = waitingFor;
         public readonly HashSet<IProjectMark> Loading = [];
      }
   }
}
