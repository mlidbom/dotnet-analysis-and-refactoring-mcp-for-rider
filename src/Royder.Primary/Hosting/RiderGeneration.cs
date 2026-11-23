using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Royder.Primary.Hosting;

/// <summary>A single supervisor, its JVM and its logs. All ownership evidence is scoped to this run directory.</summary>
internal sealed class RiderGeneration
{
   internal sealed record Plan(string Profile, string Run, string ManagedAssets, string Build, string HostHash, string Tools,
                               int RemoteDevPort, int RoyderPort, int HeapMiB, string Java);
   internal sealed record JobState(string Phase, RiderProcessIdentity Supervisor, RiderProcessIdentity? HostProcess,
                                   RiderProcessIdentity[] Members, RiderProcessIdentity[]? Observed, string? ObservationRequest,
                                   string? Error);
   internal sealed record Backend(string Solution, int InitialPort, RiderProcessIdentity Process);

   internal readonly Plan Launch;
   internal JobState State => RiderSupervisor.Read<JobState>(Path.Combine(Launch.Run, "state.json"));
   RiderGeneration(Plan launch) => Launch = launch;

   public static async Task<RiderGeneration?> Current(SharedRiderSettings settings, CancellationToken cancellation)
   {
      var pointer = Path.Combine(settings.ProfileDirectory, "current.json");
      if(!File.Exists(pointer)) return null;
      var run = RiderSupervisor.Read<string>(pointer);
      var parent = Path.GetDirectoryName(run);
      if(!string.Equals(parent, Path.Combine(settings.ProfileDirectory, "runs"), StringComparison.OrdinalIgnoreCase) ||
         !Guid.TryParseExact(Path.GetFileName(run), "N", out _))
         throw new InvalidDataException("Invalid private Rider generation directory.");
      var plan = RiderSupervisor.Read<Plan>(Path.Combine(run, "plan.json"));
      if(string.IsNullOrEmpty(plan.ManagedAssets) || !Same(plan.Profile, settings.ProfileDirectory) || !Same(plan.Run, run))
         throw new InvalidOperationException("The profile does not contain a managed Rider generation.");
      var generation = new RiderGeneration(plan);
      var timer = Stopwatch.StartNew();
      while(timer.Elapsed < TimeSpan.FromSeconds(40))
      {
         cancellation.ThrowIfCancellationRequested();
         var stateFile = Path.Combine(run, "state.json");
         if(File.Exists(stateFile))
         {
            var state = generation.State;
            var supervisorAlive = state.Supervisor.IsAlive();
            var jvmAlive = state.HostProcess?.IsAlive() == true;
            if(state.Phase == "Running" && supervisorAlive && jvmAlive && !File.Exists(Path.Combine(run,"stop.request")))
               return generation;
            if((!supervisorAlive || state.Phase is "Stopped" or "Failed") && !jvmAlive &&
               !(state.Observed ?? state.Members).Any(it => it.IsAlive()) && ProfileIsReleased(settings.ProfileDirectory))
               return null;
         }
         else if(ProfileIsReleased(settings.ProfileDirectory) && !CommandsInProgress(settings.ProfileDirectory)) return null;
         await Task.Delay(200, cancellation).ConfigureAwait(false);
      }
      throw new InvalidOperationException($"Private Rider run {run} is still starting, stopping, or has surviving unverified processes. No new JVM was started.");
   }

   static bool CommandsInProgress(string profile) => FileIsLocked(Path.Combine(profile,"commands.lock"));
   static bool ProfileIsReleased(string profile) => !FileIsLocked(Path.Combine(profile,"host.lock"));
   static bool FileIsLocked(string path)
   {
      try { using var file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); return false; }
      catch(IOException ex) when((ex.HResult & 0xffff) is 32 or 33) { return true; }
   }

   public void Adopt(SharedRiderSettings settings, bool stopping = false)
   {
      if(!stopping && (!Same(Launch.ManagedAssets,settings.AssetsDirectory) || Launch.Build != settings.ToolsBuild ||
         !Same(Launch.Tools, settings.ToolsFolder is {} shared ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(shared)) : Path.Combine(Launch.Run,"tools")) ||
         Launch.HeapMiB != settings.HeapMiB || Launch.RoyderPort != settings.BackendMcpPort ||
         Launch.RemoteDevPort != settings.RemoteDevPort || !Same(Launch.Java, Path.Combine(settings.RiderHome,"jbr","bin","java.exe"))))
         throw new InvalidOperationException("The running Rider generation uses different settings. Stop it before switching builds or ports.");
      var path = Path.Combine(Launch.Run,"owner.json");
      var previous = RiderSupervisor.Read<RiderProcessIdentity>(path);
      if(previous.IsAlive())
      {
         if(previous.Pid == Environment.ProcessId) return;
         throw new InvalidOperationException($"This private Rider still has a live owning primary (PID {previous.Pid}).");
      }
      RiderSupervisor.Write(path,RiderSupervisor.Owner());
   }

   /// <summary>
   ///   Whether a solution's health row says this generation's host serves it: its host, and a tools build from one of
   ///   its two folders. They are the folder its environment names, <see cref="Plan.Tools"/>, and the plugin's own,
   ///   which holds the pinned build when the first is a shared folder of everyday builds. Any build either holds is one
   ///   the host chose for itself.
   /// </summary>
   public bool Serves(JsonNode? solution) =>
      solution?["tools"]?["build"]?.GetValue<string>() != null &&
      Same(solution["tools"]?["host"]?.GetValue<string>(), Launch.HostHash) &&
      solution["tools"]?["folder"]?.GetValue<string>() is {} folder &&
      (Same(folder, Launch.Tools) || Same(folder, Path.Combine(Launch.Run, "plugins", "DotNetAnalysisAndRefactoringMcp", "tools")));

   public void AssertRunning()
   {
      var state = State;
      if(state.Phase != "Running" || !state.Supervisor.IsAlive() || state.HostProcess?.IsAlive() != true)
         throw new InvalidOperationException($"Private Rider stopped during the operation ({state.Phase}). {state.Error}");
   }

   public IReadOnlyList<Backend> Backends()
   {
      var found = new List<Backend>();
      foreach(var member in State.Members.Where(it => it.Name.Equals("Rider.BackendHost", StringComparison.OrdinalIgnoreCase) && it.IsAlive()))
      {
         foreach(var log in Directory.EnumerateFiles(Path.Combine(Launch.Run,"log"), $"backend.*.{member.Pid}.log"))
         {
            if(File.GetCreationTimeUtc(log).Ticks < member.Started) continue;
            using var input = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(input);
            string? solution = null;
            int port = 0;
            while(reader.ReadLine() is {} line)
            {
               if(port == 0 && line.Contains("McpShellComponent",StringComparison.Ordinal) && PortAnnouncement.Match(line) is { Success: true } announcement)
                  port = int.Parse(announcement.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
               if(!line.Contains("| RiderSolutionWatcher ",StringComparison.Ordinal)) continue;
               if(SolutionOpened.Match(line) is { Success: true } opened) solution = opened.Groups[1].Value;
               else if(line.EndsWith("| Set HasBackedSolution = false",StringComparison.Ordinal)) solution = null;
            }
            if(solution == null || port < Launch.RoyderPort || port >= Launch.RoyderPort + 10) continue;
            found.Add(new Backend(solution,port,member));
            break;
         }
      }
      return found;
   }

   static readonly Regex PortAnnouncement = new(@"\|\s*McpShellComponent\s*\|[^|]*\|\s*MCP (?:primary|peer) server started on port (\d+)\b");
   static readonly Regex SolutionOpened = new(@"\|\s*RiderSolutionWatcher\s*\|[^|]*\|\s*Open solution '(.+)'$");
   internal static bool Same(string? a,string? b) => string.Equals(a,b,StringComparison.OrdinalIgnoreCase);

   string Key(string solution) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(solution.ToUpperInvariant())));
   public string Receipt(string solution) => Path.Combine(Launch.Run,"closing-" + Key(solution) + ".json");
   public string OpenIdentity(string solution) => Path.Combine(Launch.Run,"opened-" + Key(solution) + ".json");
   public string Tag(string solution) => Path.Combine(Launch.Run,"project-" + Key(solution) + ".etag");
   public string OpenIntent(string solution) => Path.Combine(Launch.Run,"project-" + Key(solution) + ".open-id");

   /// <summary>The JVM's own discovery record. An empty result here distinguishes the last project closing from
   /// a gap in backend registrations during primary election.</summary>
   public string[]? Projects()
   {
      var jvm = State.HostProcess;
      if(jvm == null || !jvm.IsAlive()) return null;
      var file = Path.Combine(Launch.Profile,"local","JetBrains","RemoteDev","active",jvm.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
      if(!File.Exists(file)) return null;
      try
      {
         var record = RiderSupervisor.Read<JsonObject>(file);
         if(record["appPid"]?.GetValue<int>() != jvm.Pid || record["unattendedMode"]?.GetValue<bool>() != true ||
            record["backendUnresponsive"]?.GetValue<bool>() != false ||
            !Same(record["ideIdentityString"]?.GetValue<string>(),Path.Combine(Launch.Profile,"config"))) return null;
         // This build omits the projects property from the common PID status when its last project closes.
         if(!record.ContainsKey("projects")) return [];
         if(record["projects"] is not JsonArray projects) return null;
         return projects.Select(it => it?["projectPath"]?.GetValue<string>() ?? "").ToArray();
      }
      catch(FileNotFoundException) { return null; } // The JVM can exit between its PID check and its status read.
      catch(JsonException) { return null; } // Status renewal is atomic except on filesystems without atomic moves.
   }

   public async Task WaitForWorkers(Backend target, CancellationToken cancellation)
   {
      var timer = Stopwatch.StartNew();
      string evidence = $"backend {target.Process.Pid} is still alive";
      while(timer.Elapsed < TimeSpan.FromSeconds(90))
      {
         cancellation.ThrowIfCancellationRequested();
         var state = State;
         if(state.Phase is "Stopped" or "Failed" && !(state.Observed ?? state.Members).Any(it => it.IsAlive())) return;
         AssertRunning();
         if(!target.Process.IsAlive())
         {
            // Require at least one new enumeration of the kernel job after the backend exited.
            var nonce = Guid.NewGuid().ToString("N");
            RiderSupervisor.Write(Path.Combine(Launch.Run,"observe.json"), nonce);
            do
            {
               await Task.Delay(150,cancellation).ConfigureAwait(false);
               state = State;
               if(timer.Elapsed >= TimeSpan.FromSeconds(90)) throw new TimeoutException("Supervisor did not confirm fresh job membership after close.");
            } while(state.ObservationRequest != nonce && state.Phase == "Running");
            if(state.Phase != "Running") continue;
            var pending = Unreleased(state,target.Process).ToArray();
            if(pending.Length == 0) return;
            evidence = string.Join(", ",pending);
         }
         await Task.Delay(250,cancellation).ConfigureAwait(false);
      }
      throw new TimeoutException($"Cannot confirm removal is safe for '{target.Solution}': {evidence}.");
   }

   static IEnumerable<string> Unreleased(JobState state, RiderProcessIdentity target)
   {
      var history = (state.Observed ?? state.Members).ToLookup(it => it.Pid);
      foreach(var live in state.Members.Where(it => it.IsAlive()))
      {
         var current = live;
         while(true)
         {
            if(current.SameProcess(target)) { yield return $"{live.Name} PID {live.Pid}"; break; }
            if(state.HostProcess?.SameProcess(current) == true || current.Name.Equals("jetbrainsd",StringComparison.OrdinalIgnoreCase)) break;
            var parent = current.ParentPid is int id
               ? history[id].Where(it => it.Started < current.Started).MaxBy(it => it.Started) : null;
            if(parent == null) { yield return $"unverified ancestry of {live.Name} PID {live.Pid}"; break; }
            current = parent;
         }
      }
   }
}
