using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Royder.Primary.Worktrees;

namespace Royder.Primary.Hosting;

/// <summary>Owns one recoverable private Rider JVM. Commands affecting its projects are serialized across primary processes.</summary>
internal sealed class SharedRiderHost
{
   readonly SharedRiderSettings settings;
   readonly RiderSupervisor supervisor;
   readonly HttpClient http;

   public SharedRiderHost(SharedRiderSettings settings, HttpClient http)
   {
      this.settings = settings with { ProfileDirectory=Absolute(settings.ProfileDirectory), RiderHome=Absolute(settings.RiderHome),
                                     AssetsDirectory=Absolute(settings.AssetsDirectory) };
      if(OperatingSystem.IsWindows()) CheckProfile(this.settings.ProfileDirectory);
      this.http = http;
      supervisor = new RiderSupervisor(this.settings);
   }

   public async Task OpenAsync(RegisteredWorktree checkout, string solutionPath, CancellationToken cancellation)
   {
      var solution = SolutionFile(solutionPath);
      if(checkout.IsMainCheckout && !SupportsPrivateProjectStores())
         throw new InvalidOperationException("This Rider package predates private project settings. Restage it before opening a main checkout.");
      if(!File.Exists(solution)) throw new FileNotFoundException("The solution to open does not exist.",solution);
      using var operation = await Acquire(cancellation).ConfigureAwait(false);
      var generation = await RiderGeneration.Current(settings,cancellation).ConfigureAwait(false);
      var newlyLaunched = generation == null;
      if(generation == null)
      {
         await supervisor.Command("start",solution,null,cancellation).ConfigureAwait(false);
         generation = await RiderGeneration.Current(settings,cancellation).ConfigureAwait(false) ??
            throw new InvalidOperationException("The private Rider supervisor ended during launch.");
      }
      generation.Adopt(settings);
      var receipt = generation.Receipt(solution);
      if(File.Exists(receipt))
      {
         var prior = RiderSupervisor.Read<RiderGeneration.Backend>(receipt);
         if(!prior.Process.IsAlive())
         {
            await generation.WaitForWorkers(prior,cancellation).ConfigureAwait(false);
            File.Delete(generation.Tag(solution));
            File.Delete(generation.OpenIntent(solution));
         }
      }
      // The first solution also needs the JVM's disk identity and ETag for a later discard after Git removes it.
      try
      {
         var status = await Post("open",solution,generation,cancellation).ConfigureAwait(false);
         if(status != 200) throw Rejected("open",solution,status);
      }
      catch when(newlyLaunched)
      {
         // The first project has not been handed to a caller and no sibling can have opened under this lease.
         // End this job without saving stale settings, including if Git removed the tree during the initial open.
         File.WriteAllText(Path.Combine(generation.Launch.Run, "abandon.request"), "");
         await supervisor.Command("stop",null,generation.Launch.Run,CancellationToken.None).ConfigureAwait(false);
         throw;
      }
      await new RiderBackendCluster(http,generation).WaitForRouting(solution,cancellation).ConfigureAwait(false);
      var backend = generation.Backends().Single(it => RiderGeneration.Same(it.Solution,solution));
      RiderSupervisor.Write(generation.OpenIdentity(solution),backend);
      File.Delete(receipt);
   }

   sealed record Package(bool PrivateProjectStores);

   bool SupportsPrivateProjectStores() =>
      RiderSupervisor.Read<Package>(Path.Combine(settings.AssetsDirectory, "package.json")).PrivateProjectStores;

   public async Task CloseAsync(string solutionPath, bool discard, CancellationToken cancellation)
   {
      var solution = SolutionFile(solutionPath);
      using var operation = await Acquire(cancellation).ConfigureAwait(false);
      var generation = await RiderGeneration.Current(settings,cancellation).ConfigureAwait(false);
      if(generation == null) return; // No live JVM or job members remain in the persisted generation.
      generation.Adopt(settings);
      var receipt = generation.Receipt(solution);
      var before = generation.Backends();
      // The JVM may report a project directory for a solution opened through the plugin.
      // Only backend solution filenames (or an explicit filename in discovery) identify a cluster registration.
      var siblings = before.Select(it => it.Solution).Concat((generation.Projects() ?? []).Where(IsSolutionFilename).Select(Path.GetFullPath))
         .Where(it => it.Length != 0 && !RiderGeneration.Same(it,solution)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
      var existing = before.SingleOrDefault(it => RiderGeneration.Same(it.Solution,solution));
      // An open can succeed and lose its reply. Ask for the JVM's receipt for the persisted open ID.
      if(!File.Exists(generation.Tag(solution)) && File.Exists(generation.OpenIntent(solution)))
         await Post("status",solution,generation,cancellation).ConfigureAwait(false);
      var identity = File.Exists(receipt) ? receipt : generation.OpenIdentity(solution);
      var target = existing ?? (File.Exists(identity) ? RiderSupervisor.Read<RiderGeneration.Backend>(identity) : null);
      if(target == null)
      {
         if(await Post("status",solution,generation,cancellation).ConfigureAwait(false) == 404)
         {
            File.Delete(generation.OpenIntent(solution));
            return;
         }
         throw new InvalidOperationException($"The private JVM still has a project at '{solution}' with no confirmed backend identity.");
      }
      if(!RiderGeneration.Same(target.Solution,solution))
         throw new InvalidOperationException($"Cannot confirm a safe close of '{solution}': the private job has no backend PID/start time with a matching RiderSolutionWatcher solution-open record.");
      if(existing == null)
      {
         var unassigned = generation.State.Members.Where(it => it.Name.Equals("Rider.BackendHost",StringComparison.OrdinalIgnoreCase) &&
            it.IsAlive() && !it.SameProcess(target.Process) && !before.Any(backend => backend.Process.SameProcess(it))).ToArray();
         if(unassigned.Length != 0 || before.Any(backend => backend.Process.SameProcess(target.Process)))
            throw new InvalidOperationException($"Cannot identify the current backend for '{solution}' from its saved PID {target.Process.Pid}. " +
               $"Unassigned backend PIDs: {string.Join(", ",unassigned.Select(it => it.Pid))}. No close was dispatched.");
      }
      if(File.Exists(generation.Tag(solution)))
      {
         RiderSupervisor.Write(receipt,target); // Recovery must remember the incarnation even if the manager dies inside the POST.
         var status = await Post(discard ? "discard" : "close",solution,generation,cancellation).ConfigureAwait(false);
         if(status is < 200 or >= 300 && status != 404) throw Rejected("close",solution,status);
      }
      else if(!File.Exists(receipt) || target.Process.IsAlive())
         throw new InvalidOperationException("Missing the original IntelliJ project tag; cannot safely close the worktree.");
      if(siblings.Length == 0 && before.All(it => it.Process.SameProcess(target.Process)))
      {
         // Rider can keep the last full backend alive in its lobby after saving and closing its only project.
         var timer = Stopwatch.StartNew();
         var supportsProjectQuery = SupportsPrivateProjectStores();
         while(timer.Elapsed < TimeSpan.FromSeconds(12))
         {
            cancellation.ThrowIfCancellationRequested();
            var empty = supportsProjectQuery ? await Post("projects",solution,generation,cancellation).ConfigureAwait(false) == 204 :
                                              generation.Projects() is { Length: 0 };
            if(empty && generation.State.Members
               .Where(it => it.Name.Equals("Rider.BackendHost",StringComparison.OrdinalIgnoreCase) && it.IsAlive())
               .All(it => it.SameProcess(target.Process)))
            {
               await supervisor.Command("stop",null,generation.Launch.Run,cancellation).ConfigureAwait(false);
               return;
            }
            if(!target.Process.IsAlive()) break;
            await Task.Delay(250,cancellation).ConfigureAwait(false);
         }
      }
      await generation.WaitForWorkers(target,cancellation).ConfigureAwait(false);
      if(generation.State.Phase == "Running") await new RiderBackendCluster(http,generation).WaitForRouting(null,cancellation,closed:solution,siblings:siblings).ConfigureAwait(false);
      else if(siblings.Length != 0) throw new InvalidOperationException("The private Rider JVM stopped while closing a project with open siblings.");
      File.Delete(generation.Tag(solution));
      File.Delete(generation.OpenIntent(solution));
      // Leave the receipt for retries after the manager returns. A successful reopen removes it.
   }

   public async Task StopAsync(CancellationToken cancellation)
   {
      using var operation = await Acquire(cancellation).ConfigureAwait(false);
      var generation = await RiderGeneration.Current(settings,cancellation).ConfigureAwait(false);
      if(generation != null)
      {
         generation.Adopt(settings,stopping:true);
         await supervisor.Command("stop",null,generation.Launch.Run,cancellation).ConfigureAwait(false);
      }
   }

   async Task<int> Post(string verb,string solution,RiderGeneration generation,CancellationToken cancellation)
   {
      var result = await supervisor.Command(verb,solution,generation.Launch.Run,cancellation).ConfigureAwait(false);
      if(int.TryParse(result,NumberStyles.None,CultureInfo.InvariantCulture,out var status)) return status;
      throw new InvalidDataException($"Private Rider {verb} did not return an HTTP status.");
   }

   static Exception Rejected(string action,string solution,int status) =>
      new InvalidOperationException($"Private Rider refused to {action} '{solution}' (HTTP {status}).");

   async Task<FileStream> Acquire(CancellationToken cancellation)
   {
      if(!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Shared Rider hosting requires Windows.");
      var directory = Directory.CreateDirectory(settings.ProfileDirectory);
      var timer = Stopwatch.StartNew();
      while(true)
      {
         cancellation.ThrowIfCancellationRequested();
         try
         {
            var operation = new FileStream(Path.Combine(directory.FullName,"manager-operation.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            try
            {
               // A command dispatched before its primary died may still be finishing in PowerShell.
               using var previousCommand = new FileStream(Path.Combine(directory.FullName,"managed-command.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
               return operation;
            }
            catch { operation.Dispose(); throw; }
         }
         catch(IOException e) when((e.HResult & 0xffff) is 32 or 33 && timer.Elapsed < TimeSpan.FromMinutes(6))
         { await Task.Delay(150,cancellation).ConfigureAwait(false); }
      }
   }

   static string Absolute(string path) => Path.IsPathFullyQualified(path) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)) :
      throw new ArgumentException("A fully qualified Windows path is required.",nameof(path));

   internal static void CheckProfile(string path)
   {
      if(Path.GetPathRoot(path) == path) throw new ArgumentException("Use a dedicated private profile directory.");
      foreach(var folder in new[] {Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.LocalApplicationData})
      {
         var real = Path.Combine(Environment.GetFolderPath(folder),"JetBrains");
         if(RiderGeneration.Same(path,real) || path.StartsWith(real + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The managed profile must be outside the everyday JetBrains directories.");
      }
      for(var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
         if(directory.Exists && directory.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new ArgumentException($"The private Rider profile crosses a filesystem link: {directory.FullName}");
   }

   static string SolutionFile(string path)
   {
      path = Absolute(path);
      if(!IsSolutionFilename(path))
         throw new ArgumentException("A solution filename ending in .sln or .slnx is required.",nameof(path));
      return path;
   }

   static bool IsSolutionFilename(string path) => path.EndsWith(".sln",StringComparison.OrdinalIgnoreCase)
                                                 || path.EndsWith(".slnx",StringComparison.OrdinalIgnoreCase);
}
