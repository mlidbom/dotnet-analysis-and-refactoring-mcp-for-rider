using System.Diagnostics;
using System.Text.Json;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Plugin;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Screen;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

/// <summary>
///   A second Rider, with settings, caches, plugins and logs of its own and the plugin's MCP server on a port of its
///   own, <see cref="McpPort"/>. Claude starts it, tests builds in it and stops it, without touching the
///   <see cref="EverydayRider"/> that Magnus and every session work in.
/// </summary>
/// <remarks>
///   Its folders are under <c>%LOCALAPPDATA%\DotNetAnalysisAndRefactoringMcpSandbox</c>, outside the repository, so the everyday Rider,
///   which has the repository open, doesn't watch its caches change. Rider's launcher reads the folders from the
///   properties file that the <c>RIDER_PROPERTIES</c> environment variable names, and the JVM options from the file
///   that <c>RIDER_VM_OPTIONS</c> names: <see cref="JvmOptionsFile"/>.<br/>
///   <see cref="ThePeer"/> is a second sandbox, beside it, for what needs two Riders.
/// </remarks>
public sealed class SandboxRider
{
   /// <summary>The port the plugin's MCP server listens on in the sandbox. Everywhere else it's 23751 unless set otherwise.</summary>
   public const int McpPort = 23841;

   /// <summary>
   ///   How large the heap of the sandbox's frontend may grow: the installation's default, which is ample for the small
   ///   solutions the sandbox opens.
   /// </summary>
   const int HeapLimitInMegabytes = 2048;

   static readonly TimeSpan HowLongStartingMayTake = TimeSpan.FromMinutes(5);
   /// <summary>
   ///   How long closing the sandbox's windows may take to end it, before its processes are killed. Rider sometimes asks
   ///   "Confirm Exit", on every virtual desktop, which closing its windows doesn't answer (2026-11-17 and 2026-11-18).
   /// </summary>
   static readonly TimeSpan HowLongClosingMayTake = TimeSpan.FromSeconds(30);

   static readonly TimeSpan HowLongDyingMayTake = TimeSpan.FromSeconds(30);

   static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(5) };

   const string FolderName = "DotNetAnalysisAndRefactoringMcpSandbox";

   readonly RiderInstallation _rider;
   readonly string _root;
   readonly int _windowSlot;

   public SandboxRider(RiderInstallation rider) : this(rider, FolderName, windowSlot: 0, "The sandbox Rider") {}

   SandboxRider(RiderInstallation rider, string folderName, int windowSlot, string name)
   {
      _rider = rider;
      Name = name;
      _root = Path.Combine(LocalApplicationData, folderName);
      _windowSlot = windowSlot;
   }

   /// <summary>
   ///   A second sandbox, with folders of its own beside the first's, which runs as the first one's peer: its MCP server
   ///   takes the next free port after <see cref="McpPort"/> and registers its solutions with the first's, which
   ///   forwards their calls. It serves the first sandbox's <see cref="Tools"/>, and its windows go beside the first's.
   /// </summary>
   /// <remarks>
   ///   Beside the first sandbox's folder, not in it: a solution in the first sandbox is then picked out by a segment of
   ///   its path, <c>DotNetAnalysisAndRefactoringMcpSandbox</c>, that the peer's path doesn't have.
   /// </remarks>
   public static SandboxRider ThePeer(RiderInstallation rider) => new(rider, FolderName + "Peer", windowSlot: 1, "The peer sandbox Rider");

   /// <summary>Which sandbox it is, as its messages name it, at the start of a sentence.</summary>
   public string Name { get; }

   static string LocalApplicationData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

   /// <summary>The plugin as installed in the sandbox. A build installed while it runs takes effect when it restarts.</summary>
   public InstalledPlugin Plugin => new(PluginsFolder, Path.Combine(_root, "replaced-builds"));

   /// <summary>
   ///   The tools builds the sandbox's host serves, the newest built against it, instead of the everyday Riders' ones: the
   ///   sandbox starts with the host's <c>DOTNET_ANALYSIS_AND_REFACTORING_MCP_TOOLS</c> naming this folder. The first
   ///   sandbox's folder, which <see cref="ThePeer"/> serves too, as the everyday Riders share theirs.
   /// </summary>
   public ToolsBuilds Tools => new(Path.Combine(LocalApplicationData, FolderName, "tools"));

   /// <summary>
   ///   Puts <paramref name="build"/> in the sandbox. Only its tools if the sandbox has its plugin already, which it serves
   ///   from the next call on; otherwise the whole plugin, restarting the sandbox if it runs. Returns whether it
   ///   installed the plugin.
   /// </summary>
   public bool Install(PluginBuild build)
   {
      var installsThePlugin = InstallThePluginOf(build);
      Tools.Deploy(build);
      return installsThePlugin;
   }

   /// <summary>
   ///   Installs the plugin of <paramref name="build"/> unless the sandbox has it already, restarting the sandbox if
   ///   it runs; returns whether it installed it. All that <see cref="ThePeer"/> needs, as it serves the tools deployed to
   ///   the first sandbox.
   /// </summary>
   public bool InstallThePluginOf(PluginBuild build)
   {
      if(Plugin.HasThePluginOf(build)) return false;
      var wasRunning = IsRunning;
      Stop();
      Plugin.Install(build);
      if(wasRunning) Start();
      return true;
   }

   /// <summary>Where solutions to open in the sandbox go. The sandbox trusts everything under it.</summary>
   public string SolutionsFolder => Path.Combine(_root, "solutions");

   /// <summary>
   ///   The JVM options the sandbox's frontend starts with, after the installation's own: the heap limit,
   ///   <see cref="HeapLimitInMegabytes"/>.
   /// </summary>
   /// <remarks>
   ///   Without a file of its own, Rider's launcher took the everyday Riders' file, with Magnus's 16 GB heap limit and
   ///   Toolbox's options. The last <c>-Xmx</c> wins, so every sandbox could grow as large (found 2026-11-18).
   /// </remarks>
   string JvmOptionsFile => Path.Combine(_root, "rider64.exe.vmoptions");

   string PluginsFolder => Path.Combine(_root, "plugins");
   string ProcessFile => Path.Combine(_root, "running-process.txt");
   bool HasThePluginInstalled => File.Exists(Path.Combine(PluginsFolder, "DotNetAnalysisAndRefactoringMcp", "dotnet", "DotNetAnalysisAndRefactoringMcp.dll"));

   public bool IsRunning => RunningProcess() is not null;

   /// <summary>The sandbox's frontend process, unless it isn't running: everyday Riders run the same executable.</summary>
   public int? ProcessId => RunningProcess()?.Id;

   /// <summary>
   ///   Starts the sandbox unless it's running, and places its windows. Given a <paramref name="solution"/>, it opens it,
   ///   and returns once the plugin's MCP server on <see cref="McpPort"/> answers with the solution loaded and indexed:
   ///   for <see cref="ThePeer"/>, the first sandbox's, which the peer's registers its solutions with.
   /// </summary>
   /// <remarks>
   ///   The plugin's MCP server listens only while a solution is open, so without one there is nothing to wait for.
   ///   <br/>It trusts <see cref="SolutionsFolder"/> and the given solution's folder. Until 2026-11-25 it trusted only the
   ///   first, and any other solution, such as this repository's own, waited the whole <see cref="HowLongStartingMayTake"/>
   ///   on Rider's "Trust and open?" dialog.
   /// </remarks>
   public void Start(string? solution = null)
   {
      var settings = new SandboxRiderSettings(Path.Combine(_root, "config"));
      if(solution is not null && RunningProcess() is not null && !settings.Trust(solution))
         Stop(); // Its settings are written only while it doesn't run; without trust, Rider waits on its "Trust and open?" dialog.

      if(RunningProcess() is null)
      {
         settings.Write(solution is null ? [SolutionsFolder] : [SolutionsFolder, Path.GetDirectoryName(Path.GetFullPath(solution))!]);
         WriteTheFoldersFile();
         File.WriteAllLines(JvmOptionsFile, [$"-Xmx{HeapLimitInMegabytes}m"]);
         var process = Launch(solution);
         File.WriteAllText(ProcessFile, $"{process.Id};{process.StartTime.Ticks}");
      } else if(solution is not null)
      {
         Launch(solution); // A second launch hands the solution to the running sandbox and exits.
      }

      var running = RunningProcess() ?? throw new InvalidOperationException($"{Name} exited while starting. See its logs in " + Path.Combine(_root, "log"));
      WaitUntil(() => FramesOf(running).Count > 0, HowLongStartingMayTake, $"{Name} to show a window");
      PlaceWindows();
      if(solution is null || !HasThePluginInstalled) return;

      WaitUntil(() => TheMcpServerHasIndexed(solution), HowLongStartingMayTake, $"the MCP server on port {McpPort} to answer with {solution} loaded and indexed");
      PlaceWindows(); // The solution's own window has replaced Rider Home by now.
   }

   /// <summary>
   ///   Closes the sandbox's windows until it exits, as a human closing them would, and kills its processes if that
   ///   hasn't ended it within <see cref="HowLongClosingMayTake"/>: a sandbox is disposable, and one left running holds
   ///   gigabytes of Magnus's memory.
   /// </summary>
   public void Stop()
   {
      var running = RunningProcess();
      if(running is null) return;
      if(!ClosedByItsWindows(running))
      {
         running.Kill(entireProcessTree: true);
         WaitUntil(() => running.HasExited, HowLongDyingMayTake, $"{Name} to die");
      }

      File.Delete(ProcessFile);
   }

   bool ClosedByItsWindows(Process running)
   {
      var closing = Stopwatch.StartNew();
      while(closing.Elapsed < HowLongClosingMayTake)
      {
         if(running.HasExited) return true;
         foreach(var window in FramesOf(running)) window.AskToClose();
         if(running.WaitForExit(TimeSpan.FromSeconds(5))) return true;
      }

      return running.HasExited;
   }

   /// <summary>
   ///   Whether the sandbox runs, where its windows are next to the development Rider's, and what its MCP server's
   ///   health endpoint answers.
   /// </summary>
   public IReadOnlyList<string> Status()
   {
      if(RunningProcess() is not {} running) return [$"{Name} isn't running."];

      return ScreenThread.Run(() => (IReadOnlyList<string>)
                              [
                                 $"{Name} runs as process {running.Id}.",
                                 .. FramesOf(running).Select(it => "  sandbox window: " + it.Description),
                                 "  development Rider window: " + (SandboxWindowPlacement.TheDevelopmentRiderWindow(running.Id)?.Description ?? "none open"),
                                 "  MCP health: " + (HasThePluginInstalled ? TheMcpServersHealth() ?? "nothing listens on port " + McpPort : "the plugin isn't installed")
                              ]);
   }

   /// <summary>Puts the sandbox's windows where <see cref="SandboxWindowPlacement"/> says.</summary>
   public void PlaceWindows()
   {
      var running = RunningProcess() ?? throw new InvalidOperationException($"{Name} isn't running.");
      SandboxWindowPlacement.Apply(FramesOf(running), running.Id, _windowSlot);
   }

   /// <remarks>
   ///   Through the shell, so Rider gets none of this process's handles. Started directly, it inherited the console
   ///   handles: Rider's output came out in the tool's, and a pipe the tool's output went into stayed open as long as
   ///   Rider ran. The shell can't take environment variables for the one start, so they're set on this process, whose
   ///   environment the started one inherits.
   /// </remarks>
   Process Launch(string? solution)
   {
      Environment.SetEnvironmentVariable("RIDER_PROPERTIES", Path.Combine(_root, "idea.properties"));
      Environment.SetEnvironmentVariable("RIDER_VM_OPTIONS", JvmOptionsFile);
      Environment.SetEnvironmentVariable("DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT", McpPort.ToString());
      Environment.SetEnvironmentVariable("DOTNET_ANALYSIS_AND_REFACTORING_MCP_TOOLS", Tools.Folder);
      var startInformation = new ProcessStartInfo(_rider.Launcher) { UseShellExecute = true };
      if(solution is not null) startInformation.ArgumentList.Add(solution);
      return Process.Start(startInformation) ?? throw new InvalidOperationException($"Could not start {_rider.Launcher}.");
   }

   void WriteTheFoldersFile()
   {
      var root = _root.Replace('\\', '/');
      File.WriteAllLines(Path.Combine(_root, "idea.properties"),
                         [
                            $"idea.config.path={root}/config",
                            $"idea.system.path={root}/system",
                            $"idea.plugins.path={root}/plugins",
                            $"idea.log.path={root}/log"
                         ]);
   }

   /// <summary>The sandbox's frontend process, which owns its windows, unless it has exited.</summary>
   Process? RunningProcess()
   {
      if(!File.Exists(ProcessFile)) return null;
      var recorded = File.ReadAllText(ProcessFile).Split(';');
      var process = Process.GetProcesses().FirstOrDefault(it => it.Id == int.Parse(recorded[0]));
      return process is not null && process.StartTime.Ticks == long.Parse(recorded[1]) ? process : null;
   }

   /// <summary>The sandbox's windows apart from its splash screen.</summary>
   static IReadOnlyList<TopLevelWindow> FramesOf(Process process) =>
      [.. TopLevelWindow.AllVisible().Where(it => it.ProcessId == process.Id && it.Title.Length > 0 && it.Title != "splash")];

   static bool TheMcpServerHasIndexed(string solution)
   {
      if(TheMcpServersHealth() is not {} health) return false;
      using var document = JsonDocument.Parse(health);
      // A peer that doesn't answer is listed without "indexing", as unreachable.
      return document.RootElement.GetProperty("solutions").EnumerateArray()
                     .Any(it => string.Equals(it.GetProperty("path").GetString(), solution, StringComparison.OrdinalIgnoreCase)
                             && it.TryGetProperty("indexing", out var indexing) && !indexing.GetBoolean());
   }

   /// <summary>What the plugin's <c>/health</c> endpoint answers, or null while nothing listens on the port.</summary>
   /// <remarks>The plugin listens only while a solution is open.</remarks>
   static string? TheMcpServersHealth()
   {
      try
      {
         return HealthClient.GetStringAsync($"http://127.0.0.1:{McpPort}/health").GetAwaiter().GetResult();
      }
      catch(HttpRequestException exception) when(exception.HttpRequestError == HttpRequestError.ConnectionError)
      {
         return null;
      }
   }

   static void WaitUntil(Func<bool> condition, TimeSpan timeout, string whatItWaitsFor)
   {
      var stopwatch = Stopwatch.StartNew();
      while(!condition())
      {
         if(stopwatch.Elapsed > timeout) throw new TimeoutException($"Gave up after {timeout} waiting for {whatItWaitsFor}.");
         Thread.Sleep(500);
      }
   }
}
