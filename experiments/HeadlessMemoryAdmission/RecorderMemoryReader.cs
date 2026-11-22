using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Royder.MemoryProbe;

/// <summary>The five memory-admission fields from one timestamped recorder sample.</summary>
public sealed record RecorderMemorySample(DateTimeOffset At, double? AvailableMiB, double? PhysicalMiB,
                                         double? CommittedGiB, double? CommitLimitGiB)
{
   public bool CanEvaluate => AvailableMiB is >= 0 && PhysicalMiB is > 0 && AvailableMiB <= PhysicalMiB &&
                              CommittedGiB is >= 0 && CommitLimitGiB is > 0;
   public double? PhysicalUsePercent => CanEvaluate ? 100 * (1 - AvailableMiB / PhysicalMiB) : null;
   public double? CommitPercent => CanEvaluate ? 100 * CommittedGiB / CommitLimitGiB : null;

   internal static RecorderMemorySample Read(JsonElement root)
   {
      double? Number(string name) => root.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number &&
                                     field.TryGetDouble(out var value) && double.IsFinite(value) ? value : null;
      return new(root.GetProperty("at").GetDateTimeOffset(), Number("availableMB"), Number("physicalMB"),
                 Number("committedGB"), Number("commitLimitGB"));
   }
}

/// <summary>A read-only client for the recorder in the caller's Windows login session.
/// A lost connection invalidates the previous stream until the new stream supplies fresh evidence.</summary>
public sealed class RecorderMemoryReader(string pipeName)
{
   public static string PipeNameForThisSession()
   {
      using var process = Process.GetCurrentProcess();
      return $"MachineStallRecorder.LiveSamples.{process.SessionId}";
   }

   public async Task FollowAsync(Action<string> connected, Action<string> unavailable,
                                 Action<RecorderMemorySample, JsonElement> sampleReceived,
                                 CancellationToken cancellation)
   {
      while(!cancellation.IsCancellationRequested)
      {
         using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.In,
                                                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
         string? failure = await Connect(pipe, cancellation);
         if(failure == null)
         {
            connected(pipeName);
            using var lines = new StreamReader(pipe, new UTF8Encoding(false, true));
            while(failure == null)
            {
               var next = await ReadLine(lines, cancellation);
               failure = next.Failure;
               if(next.Line == null) break;

               JsonDocument? document = null;
               RecorderMemorySample sample;
               try
               {
                  document = JsonDocument.Parse(next.Line);
                  sample = RecorderMemorySample.Read(document.RootElement);
               }
               catch(Exception error) when(error is JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
               {
                  document?.Dispose();
                  unavailable("invalid sample: " + error.Message);
                  continue; // Keep reading later records; reconnecting would replay the same malformed line.
               }

               // The callback may clone the element if it must outlive this invocation.
               using(document) sampleReceived(sample, document.RootElement);
            }
         }
         unavailable(failure!);
         await Task.Delay(TimeSpan.FromMilliseconds(500), cancellation);
      }
   }

   static async Task<string?> Connect(NamedPipeClientStream pipe, CancellationToken cancellation)
   {
      try { await pipe.ConnectAsync(750, cancellation); return null; }
      catch(TimeoutException) { return "connection timed out"; }
      catch(IOException failure) { return failure.Message; }
      catch(UnauthorizedAccessException failure) { return failure.Message; }
   }

   static async Task<(string? Line, string? Failure)> ReadLine(StreamReader lines, CancellationToken cancellation)
   {
      using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
      deadline.CancelAfter(TimeSpan.FromSeconds(5));
      try
      {
         var line = await lines.ReadLineAsync(deadline.Token);
         return (line, line == null ? "end of stream" : null);
      }
      catch(OperationCanceledException) when(!cancellation.IsCancellationRequested) { return (null, "no complete sample line for five seconds"); }
      catch(IOException failure) { return (null, failure.Message); }
      catch(DecoderFallbackException failure) { return (null, failure.Message); }
   }
}
