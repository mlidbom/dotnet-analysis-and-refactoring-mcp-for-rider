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
///   properties file that the <c>RIDER_PROPERTIES</c> environment variable names.
/// </remarks>
public sealed class SandboxRider
{
   /// <summary>The port the plugin's MCP server listens on in the sandbox. Everywhere else it's 23751 unless set otherwise.</summary>
   public const int McpPort = 23841;

   static readonly TimeSpan HowLongStartingMayTake = TimeSpan.FromMinutes(5);
   static readonly TimeSpan HowLongStoppingMayTake = TimeSpan.FromMinutes(2);

   static readonly HttpClient HealthClient = new() { Timeout = TimeSpan.FromSeconds(5) };

   readonly RiderInstallation _rider;
   readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotNetAnalysisAndRefactoringMcpSandbox");

   public SandboxRider(RiderInstallation rider) => _rider = rider;

   /// <summary>The plugin as installed in the sandbox. A build installed while it runs takes effect when it restarts.</summary>
   public InstalledPlugin Plugin => new(PluginsFolder, Path.Combine(_root, "replaced-builds"));

   /// <summary>
   ///   The tools builds the sandbox's host serves, the newest built against it, instead of the everyday Riders' ones: the
   ///   sandbox starts with the host's <c>DOTNET_ANALYSIS_AND_REFACTORING_MCP_TOOLS</c> naming this folder.
   /// </summary>
   public ToolsBuilds Tools => new(Path.Combine(_root, "tools"));

   /// <summary>
   ///   Puts <paramref name="build"/> in the sandbox. Only its tools if the sandbox has its host already, which it serves
   ///   from the next call on; otherwise the whole plugin, restarting the sandbox if it runs. Returns whether it
   ///   installed the plugin.
   /// </summary>
   public bool Install(PluginBuild build)
   {
      var installsThePlugin = !Plugin.HasTheHostOf(build);
      if(installsThePlugin)
      {
         var wasRunning = IsRunning;
         Stop();
         Plugin.Install(build);
         Tools.Deploy(build);
         if(wasRunning) Start();
      } else
      {
         Tools.Deploy(build);
      }

      return installsThePlugin;
   }

   /// <summary>Where solutions to open in the sandbox go. The sandbox trusts everything under it.</summary>
   public string SolutionsFolder => Path.Combine(_root, "solutions");

   string PluginsFolder => Path.Combine(_root, "plugins");
   string ProcessFile => Path.Combine(_root, "running-process.txt");
   bool HasThePluginInstalled => File.Exists(Path.Combine(PluginsFolder, "DotNetAnalysisAndRefactoringMcp", "dotnet", "DotNetAnalysisAndRefactoringMcp.dll"));

   public bool IsRunning => RunningProcess() is not null;

   /// <summary>The sandbox's frontend process, unless it isn't running: everyday Riders run the same executable.</summary>
   public int? ProcessId => RunningProcess()?.Id;

   /// <summary>
   ///   Starts the sandbox unless it's running, and places its windows. Given a <paramref name="solution"/>, it opens it,
   ///   and returns once the plugin's MCP server answers with the solution loaded and indexed.
   /// </summary>
   /// <remarks>The plugin's MCP server listens only while a solution is open, so without one there is nothing to wait for.</remarks>
   public void Start(string? solution = null)
   {
      if(RunningProcess() is null)
      {
         new SandboxRiderSettings(Path.Combine(_root, "config")).Write(SolutionsFolder);
         WriteTheFoldersFile();
         var process = Launch(solution);
         File.WriteAllText(ProcessFile, $"{process.Id};{process.StartTime.Ticks}");
      } else if(solution is not null)
      {
         Launch(solution); // A second launch hands the solution to the running sandbox and exits.
      }

      var running = RunningProcess() ?? throw new InvalidOperationException("The sandbox Rider exited while starting. See its logs in " + Path.Combine(_root, "log"));
      WaitUntil(() => FramesOf(running).Count > 0, HowLongStartingMayTake, "the sandbox Rider to show a window");
      PlaceWindows();
      if(solution is null || !HasThePluginInstalled) return;

      WaitUntil(() => TheMcpServerHasIndexed(solution), HowLongStartingMayTake, $"the sandbox's MCP server to answer with {solution} loaded and indexed");
      PlaceWindows(); // The solution's own window has replaced Rider Home by now.
   }

   /// <summary>Closes the sandbox's windows until it exits, as a human closing them would.</summary>
   public void Stop()
   {
      var running = RunningProcess();
      if(running is null) return;
      WaitUntil(() =>
                {
                   if(running.HasExited) return true;
                   foreach(var window in FramesOf(running)) window.AskToClose();
                   return running.WaitForExit(TimeSpan.FromSeconds(5));
                },
                HowLongStoppingMayTake,
                "the sandbox Rider to exit");
      File.Delete(ProcessFile);
   }

   /// <summary>
   ///   Whether the sandbox runs, where its windows are next to the development Rider's, and what its MCP server's
   ///   health endpoint answers.
   /// </summary>
   public IReadOnlyList<string> Status()
   {
      if(RunningProcess() is not {} running) return ["The sandbox Rider isn't running."];

      return ScreenThread.Run(() => (IReadOnlyList<string>)
                              [
                                 $"The sandbox Rider runs as process {running.Id}.",
                                 .. FramesOf(running).Select(it => "  sandbox window: " + it.Description),
                                 "  development Rider window: " + (SandboxWindowPlacement.TheDevelopmentRiderWindow(running.Id)?.Description ?? "none open"),
                                 "  MCP health: " + (HasThePluginInstalled ? TheMcpServersHealth() ?? "nothing listens on port " + McpPort : "the plugin isn't installed")
                              ]);
   }

   /// <summary>Puts the sandbox's windows where <see cref="SandboxWindowPlacement"/> says.</summary>
   public void PlaceWindows()
   {
      var running = RunningProcess() ?? throw new InvalidOperationException("The sandbox Rider isn't running.");
      SandboxWindowPlacement.Apply(FramesOf(running), running.Id);
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
      return document.RootElement.GetProperty("solutions").EnumerateArray()
                     .Any(it => string.Equals(it.GetProperty("path").GetString(), solution, StringComparison.OrdinalIgnoreCase)
                             && !it.GetProperty("indexing").GetBoolean());
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
