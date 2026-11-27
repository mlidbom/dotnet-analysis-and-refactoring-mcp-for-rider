using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RemoteDevHost;

namespace Royder.Primary.Hosting;

/// <summary>Commands to the packaged PowerShell launcher. The separate supervisor holds the Windows job handle.</summary>
internal sealed class RiderSupervisor(SharedRiderSettings settings)
{
   internal static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

   internal static T Read<T>(string path)
   {
      using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
      return JsonSerializer.Deserialize<T>(stream, Json) ?? throw new InvalidDataException($"Empty Rider record: {path}");
   }

   internal static void Write<T>(string path, T value)
   {
      var pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
      try
      {
         File.WriteAllBytes(pending, JsonSerializer.SerializeToUtf8Bytes(value));
         FileReplacement.Replace(pending, path);
      }
      finally { File.Delete(pending); }
   }

   internal static object Owner()
   {
      using var self = Process.GetCurrentProcess();
      return new { pid = self.Id, started = self.StartTime.ToUniversalTime().Ticks };
   }

   sealed record Reply(bool Ok, int? Status, string? Error);

   public async Task<string> Command(string operation, string? solution, string? generation, CancellationToken cancellation)
   {
      cancellation.ThrowIfCancellationRequested();
      var script = Path.Combine(settings.AssetsDirectory, "launcher", "ManagedRider.ps1");
      if(!File.Exists(script)) throw new FileNotFoundException("Stage the private Rider host package before starting the primary.", script);
      var start = new ProcessStartInfo("pwsh.exe") { UseShellExecute = false, CreateNoWindow = true,
         RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
      start.Environment["ROYDER_MANAGED_OWNER"] = JsonSerializer.Serialize(Owner());
      var resultFile = Path.Combine(settings.ProfileDirectory, "command-" + Guid.NewGuid().ToString("N") + ".json");
      foreach(var arg in new[] {"-NoProfile", "-NonInteractive", "-File", script, "-Operation", operation,
                                "-Profile", settings.ProfileDirectory})
         start.ArgumentList.Add(arg);
      if(solution != null) Add("-Solution", solution);
      if(generation != null) Add("-Generation", generation);
      Add("-ResultFile",resultFile);
      if(operation == "start")
      {
         Add("-RiderHome", settings.RiderHome);
         Add("-ToolsBuild", settings.ToolsBuild);
         if(settings.ToolsFolder != null) Add("-ToolsFolder", settings.ToolsFolder);
         Add("-RemoteDevPort", settings.RemoteDevPort.ToString(CultureInfo.InvariantCulture));
         Add("-RoyderPort", settings.BackendMcpPort.ToString(CultureInfo.InvariantCulture));
         Add("-HeapMiB", settings.HeapMiB.ToString(CultureInfo.InvariantCulture));
      }
      using var process = Process.Start(start) ?? throw new IOException("PowerShell did not start.");
      process.StandardInput.Close();
      using var drain = new CancellationTokenSource();
      var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null,drain.Token);
      var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null,drain.Token);
      // Once dispatched, let the bounded command finish even when the caller goes away. A close can already be saving a project.
      using var limit = new CancellationTokenSource(TimeSpan.FromMinutes(6));
      try
      {
         try { await process.WaitForExitAsync(limit.Token).ConfigureAwait(false); }
         catch(OperationCanceledException) when(limit.IsCancellationRequested)
         {
            process.Kill(); // Only our CLI, never its independent job supervisor.
            throw new TimeoutException($"Private Rider {operation} did not finish within six minutes. Check the profile's run state.");
         }
         if(!File.Exists(resultFile)) throw new InvalidOperationException($"The {operation} launcher exited without a result (exit {process.ExitCode}).");
         var reply = Read<Reply>(resultFile);
         if(!reply.Ok || process.ExitCode != 0) throw new InvalidOperationException($"Private Rider {operation} failed: {reply.Error}");
         return reply.Status?.ToString(CultureInfo.InvariantCulture) ?? "";
      }
      finally
      {
         drain.Cancel();
         try { await Task.WhenAll(stdout,stderr).ConfigureAwait(false); }
         catch(OperationCanceledException) when(drain.IsCancellationRequested) { }
         File.Delete(resultFile);
      }

      void Add(string name, string value) { start.ArgumentList.Add(name); start.ArgumentList.Add(value); }
   }
}
