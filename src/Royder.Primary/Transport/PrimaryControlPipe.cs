using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Royder.Primary.Transport;

/// <summary>
///   The local CLI's way to the primary in this Windows login: to remove a worktree, to stop gracefully, and for a
///   successor to take over its <see cref="PublicPort"/>.
/// </summary>
sealed class PrimaryControlPipe(PrimarySettings settings, PrimaryRequests requests, PublicPort port, IHostApplicationLifetime lifetime,
                                ILogger<PrimaryControlPipe> log) : BackgroundService
{
   /// <summary>The name from when the pipe only removed worktrees: every package's CLI reaches every other package's primary by it.</summary>
   static string Name(int port) => $"Royder.Primary.RemoveWorktree.{port}";
   static readonly Encoding Utf8 = new UTF8Encoding(false, true);

   public static async Task Remove(int port, string path)
   {
      var reply = await Send(port, JsonSerializer.Serialize(Path.GetFullPath(path)));
      if(!reply.Removed) throw new InvalidOperationException(reply.Error ?? "Missing removal confirmation.");
   }

   public static async Task Stop(int port)
   {
      var reply = await Send(port, """{"command":"stop"}""");
      if(!reply.Stopping) throw new InvalidOperationException(reply.Error ?? "Missing shutdown confirmation.");
   }

   /// <summary>Asks the primary on <paramref name="port"/> to hand the port over to this process.</summary>
   public static async Task<PortHandOver> TakeOver(int port)
   {
      Reply reply;
      try { reply = await Send(port, JsonSerializer.Serialize(new { command = "hand-over", processId = Environment.ProcessId })); }
      catch(TimeoutException) { throw new InvalidOperationException($"Port {port} is in use, and no primary on it answers its control pipe."); }
      return reply.HandedOver ?? throw new InvalidOperationException($"The primary on port {port} didn't hand it over: {reply.Error ?? "it said nothing of it."}");
   }

   static async Task<Reply> Send(int port, string command)
   {
      using var pipe = new NamedPipeClientStream(".", Name(port), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
      await pipe.ConnectAsync(5000);
      using var writer = new StreamWriter(pipe, Utf8, leaveOpen: true) { AutoFlush = true };
      using var reader = new StreamReader(pipe, Utf8, leaveOpen: true);
      await writer.WriteLineAsync(command);
      return JsonSerializer.Deserialize<Reply>(await reader.ReadLineAsync() ?? throw new IOException("The primary closed its control connection."))
             ?? throw new InvalidDataException("Missing primary control reply.");
   }

   protected override async Task ExecuteAsync(CancellationToken cancel)
   {
      while(!cancel.IsCancellationRequested && !port.HasBeenHandedOver)
      {
         using var pipe = await Opened(cancel);
         await pipe.WaitForConnectionAsync(cancel);
         try { await Answer(pipe, cancel); }
         catch(IOException error) { log.LogWarning("Could not deliver the local control result: {Reason}", error.Message); }
      }
      if(!port.HasBeenHandedOver) return;
      // The successor answers on the pipe from now on.
      await Task.Delay(PublicPort.TimeForClientsToLetGo, cancel);
      log.LogInformation("Stopping: the successor serves the public port, and the clients have had {Seconds} seconds to let go of this primary.",
         PublicPort.TimeForClientsToLetGo.TotalSeconds);
      lifetime.StopApplication();
   }

   /// <summary>The pipe's next instance. Its name is taken until the primary that handed this one the port has answered on it.</summary>
   async Task<NamedPipeServerStream> Opened(CancellationToken cancel)
   {
      while(true)
         try
         {
            return new NamedPipeServerStream(Name(settings.Port), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
               PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
         }
         catch(IOException busy) when((busy.HResult & 0xffff) == ErrorPipeBusy && !port.Predecessor.HasStopped) { await Task.Delay(50, cancel); }
   }

   const int ErrorPipeBusy = 231;

   async Task Answer(NamedPipeServerStream pipe, CancellationToken cancel)
   {
      using var reader = new StreamReader(pipe, Utf8, leaveOpen: true);
      using var writer = new StreamWriter(pipe, Utf8, leaveOpen: true) { AutoFlush = true };
      Reply reply;
      try
      {
         using var input = CancellationTokenSource.CreateLinkedTokenSource(cancel);
         input.CancelAfter(TimeSpan.FromSeconds(5));
         using var command = JsonDocument.Parse(await reader.ReadLineAsync(input.Token) ?? "");
         if(command.RootElement.ValueKind == JsonValueKind.String)
         {
            // Earlier remove-worktree clients send the absolute path as a JSON string.
            await requests.Remove(command.RootElement.GetString() ?? "", cancel);
            reply = new(true, null);
         }
         else if(Verb(command) == "stop")
            reply = new(false, null, Stopping: true);
         else if(Verb(command) == "hand-over")
         {
            var successor = command.RootElement.GetProperty("processId").GetInt32();
            reply = new(false, null, HandedOver: port.HandOverTo(successor));
            log.LogInformation("Handed the public port over to the primary in process {Successor}.", successor);
         }
         else throw new ArgumentException("Unknown primary control command.");
      }
      catch(Exception error) when(!cancel.IsCancellationRequested) { reply = new(false, error.GetBaseException().Message); }
      await writer.WriteLineAsync(JsonSerializer.Serialize(reply));
      if(reply.Stopping) lifetime.StopApplication();

      static string? Verb(JsonDocument command) =>
         command.RootElement.ValueKind == JsonValueKind.Object && command.RootElement.TryGetProperty("command", out var verb) ? verb.GetString() : null;
   }

   sealed record Reply(bool Removed, string? Error, bool Stopping = false, PortHandOver? HandedOver = null);
}
