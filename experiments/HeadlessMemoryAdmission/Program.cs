using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Royder.MemoryProbe;

var simulate = args.Contains("--exercise-reconnect");
var pipeName = simulate ? "Royder.MemoryProbe." + Guid.NewGuid().ToString("N") : RecorderMemoryReader.PipeNameForThisSession();
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(simulate ? 9 : 8));
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
var standIn = simulate ? ExerciseRestart() : Task.CompletedTask;
var stopwatch = Stopwatch.StartNew();
int connection = 0;
void Print(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
try
{
   await new RecorderMemoryReader(pipeName).FollowAsync(
      name => Print(new { kind = "connected", connection = ++connection, name, receivedAt = DateTimeOffset.Now, elapsed = stopwatch.Elapsed.TotalSeconds }),
      reason => Print(new { kind = "unavailable", connection, reason, elapsed = stopwatch.Elapsed.TotalSeconds }),
      (sample, json) => Print(new { kind = "sample", connection, receivedAt = DateTimeOffset.Now, elapsed = stopwatch.Elapsed.TotalSeconds,
                                   sample, keys = json.EnumerateObject().Select(it => it.Name).ToArray() }),
      stop.Token);
}
catch(OperationCanceledException) when(stop.IsCancellationRequested) { }
await standIn;

async Task ExerciseRestart()
{
   // Only this run's private pipe is opened for writing.
   await Task.Delay(1200);
   for(int generation=0; generation<2; generation++)
   {
      using(var server = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte,
                                                   PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly))
      {
         await server.WaitForConnectionAsync(stop.Token);
         using var writer = new StreamWriter(server, new UTF8Encoding(false)) { AutoFlush = true };
         for(int i=0; i<3; i++)
         {
            if(generation==0 && i==1) await writer.WriteLineAsync("{broken}");
            await writer.WriteLineAsync(JsonSerializer.Serialize(new { at = DateTimeOffset.Now, availableMB = 8000 + generation,
               physicalMB = 16000, committedGB = 10, commitLimitGB = 24, additionalField = "ignored" }));
            await Task.Delay(100, stop.Token);
         }
      }
      await Task.Delay(1200, stop.Token);
   }
}
