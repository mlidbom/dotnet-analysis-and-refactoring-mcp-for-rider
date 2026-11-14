using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Application;
using JetBrains.Application.Parts;
using JetBrains.Lifetimes;
using JetBrains.Util;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Swapping;

/// <summary>
///   The tools build the host serves calls with: the newest one built against this host, which it looks for at every
///   call and every <see cref="LookEvery"/>, so a new build is served from the next call on, with no Rider restart, and
///   <see cref="Swapped"/> tells the MCP clients about its tools even when no call comes.
/// </summary>
/// <remarks>
///   <para>
///     It looks in two <see cref="ToolsBuildFolder"/>s, and serves the newest build of the first that has one for this
///     host: the folder <see cref="FolderVariable"/> names, or else <see cref="DevelopmentFolder"/>, which the
///     development tools deploy to; then the plugin's own <c>tools</c> folder, which holds the build it was installed
///     with.
///   </para>
///   <para>
///     A build that a newer one replaced is unloaded when its calls are done, and then checked until the garbage
///     collector frees it. One it doesn't free shows in <see cref="Status"/> and in a note on every answer, since
///     something keeps it, and its memory, until Rider restarts: see the rules in <c>dev_docs/swappable-tools.md</c>.
///   </para>
/// </remarks>
[ShellComponent(Instantiation.ContainerAsyncAnyThreadSafe)]
public sealed class SwappableTools
{
  /// <summary>The environment variable that names a folder of tools builds to use instead of <see cref="DevelopmentFolder"/>.</summary>
  public const string FolderVariable = "DOTNET_ANALYSIS_AND_REFACTORING_MCP_TOOLS";

  /// <summary>How long after unloading a build it's checked whether the garbage collector has freed it.</summary>
  static readonly TimeSpan[] FreedChecks = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];

  /// <summary>How often it looks for a new build between calls: the folders' write times, unless one changed.</summary>
  static readonly TimeSpan LookEvery = TimeSpan.FromSeconds(2);

  readonly ILogger _logger;
  readonly string _host;
  readonly IReadOnlyList<ToolsBuildFolder> _folders;
  readonly object _gate = new();
  readonly List<string> _beingFreed = [];
  readonly List<string> _notFreed = [];
  LoadedToolsBuild _current;
  string _failedBuild;
  string _failure;

  public SwappableTools(Lifetime lifetime, ILogger logger)
  {
    _logger = logger;
    var hostAssembly = typeof(SwappableTools).Assembly.Location;
    _host = ToolsBuildFolder.HostIdentity(hostAssembly);
    var firstFolder = Environment.GetEnvironmentVariable(FolderVariable) is { Length: > 0 } named ? named : DevelopmentFolder;
    _folders = [new ToolsBuildFolder(firstFolder), new ToolsBuildFolder(PluginsOwnFolder(hostAssembly))];

    var looking = new Timer(_ => LookForANewBuild(), null, LookEvery, LookEvery);
    lifetime.OnTermination(() => looking.Dispose());
  }

  /// <summary>
  ///   Raised on a thread of the pool after a new build is swapped in, or the first one loaded: its tools may differ
  ///   from the ones the clients were told of.
  /// </summary>
  public event Action Swapped;

  /// <summary>Where the development tools deploy the builds that Magnus's everyday Riders serve.</summary>
  public static string DevelopmentFolder =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotNetAnalysisAndRefactoringMcp", "tools");

  /// <summary>The host is <c>&lt;plugin&gt;\dotnet\&lt;host&gt;.dll</c>, and the plugin's builds are in <c>&lt;plugin&gt;\tools</c>.</summary>
  static string PluginsOwnFolder(string hostAssembly) => Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(hostAssembly)), "tools");

  /// <summary>
  ///   A call counted in on the newest build, which is loaded first if it's new. Dispose it when the call is done.
  /// </summary>
  public ToolsLease Lease()
  {
    while (true)
    {
      var build = CurrentBuild();
      if (build.TryEnter()) return new ToolsLease(build);
    }
  }

  /// <summary>The build served, the builds swapped out and not freed yet, and a build that failed to load.</summary>
  public JObject Status()
  {
    lock (_gate)
      return new JObject
      {
        ["build"] = _current?.Name,
        ["folder"] = Path.GetDirectoryName(_current?.Folder),
        ["beingFreed"] = new JArray(_beingFreed),
        ["notFreed"] = new JArray(_notFreed),
        ["failedToLoad"] = _failedBuild is null ? null : new JObject { ["build"] = Path.GetFileName(_failedBuild), ["error"] = _failure }
      };
  }

  /// <summary>What a caller should know about the builds: one that failed to load, and ones that weren't freed.</summary>
  public IReadOnlyList<string> Notes()
  {
    lock (_gate)
    {
      var notes = new List<string>();
      if (_failedBuild is not null)
        notes.Add($"note: the newest tools build, {Path.GetFileName(_failedBuild)}, failed to load ({_failure}); answered by {_current?.Name}");
      if (_notFreed.Count > 0)
        notes.Add($"note: tools builds swapped out but never freed, whose memory Rider keeps until it restarts: {string.Join(", ", _notFreed)}");
      return notes;
    }
  }

  /// <summary>Swaps in a new build if there is one. A failure to load is in <see cref="Status"/> and the notes already.</summary>
  void LookForANewBuild()
  {
    try
    {
      CurrentBuild();
    }
    catch (Exception exception)
    {
      _logger.Verbose($"DotNetAnalysisAndRefactoringMcp: no tools build to serve: {exception.Message}");
    }
  }

  LoadedToolsBuild CurrentBuild()
  {
    lock (_gate)
    {
      var newest = _folders.Select(folder => folder.NewestBuildFor(_host)).FirstOrDefault(build => build is not null);
      if (newest is null && _current is null)
        throw new InvalidOperationException($"There is no tools build for this build of the host in {string.Join(" or ", _folders.Select(it => it.Path))}.");

      if (newest is null || newest == _current?.Folder || newest == _failedBuild)
        return _current ?? throw new InvalidOperationException($"The tools build {_failedBuild} failed to load: {_failure}");

      try
      {
        SwapTo(LoadedToolsBuild.Load(newest));
      }
      catch (Exception exception)
      {
        _failedBuild = newest;
        _failure = $"{exception.GetType().Name}: {exception.Message}";
        _logger.Warn($"DotNetAnalysisAndRefactoringMcp: the tools build {newest} failed to load: {exception}");
        if (_current is null) throw new InvalidOperationException($"The tools build {newest} failed to load: {_failure}");
      }

      return _current;
    }
  }

  void SwapTo(LoadedToolsBuild build)
  {
    var previous = _current;
    _current = build;
    _failedBuild = null;
    _failure = null;
    _logger.Info($"DotNetAnalysisAndRefactoringMcp: serving the tools build {build.Folder}");
    Task.Run(RaiseSwapped);
    if (previous is null) return;

    var name = previous.Name;
    _beingFreed.Add(name);
    previous.Retire(context => WaitUntilFreed(name, context));
  }

  void RaiseSwapped()
  {
    try
    {
      Swapped?.Invoke();
    }
    catch (Exception exception)
    {
      _logger.Warn($"DotNetAnalysisAndRefactoringMcp: telling of a swapped tools build failed: {exception}");
    }
  }

  /// <summary>
  ///   Collects garbage a few times, at growing intervals, until the unloaded build's load context is gone. A full
  ///   collection pauses Rider's backend briefly, and happens only after a swap.
  /// </summary>
  void WaitUntilFreed(string build, WeakReference context) =>
    Task.Run(async () =>
    {
      foreach (var wait in FreedChecks)
      {
        await Task.Delay(wait).ConfigureAwait(false);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        if (context.IsAlive) continue;

        lock (_gate) _beingFreed.Remove(build);
        _logger.Info($"DotNetAnalysisAndRefactoringMcp: the tools build {build} was freed");
        return;
      }

      lock (_gate)
      {
        _beingFreed.Remove(build);
        _notFreed.Add(build);
      }
      _logger.Warn($"DotNetAnalysisAndRefactoringMcp: the tools build {build} was not freed after it was swapped out: something keeps one of its objects or types");
    });
}
