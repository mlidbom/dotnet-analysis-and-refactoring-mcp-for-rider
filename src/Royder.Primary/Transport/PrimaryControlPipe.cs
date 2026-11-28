using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Royder.Primary.Transport;

/// <summary>The local CLI asks the primary in this Windows login to remove a worktree or shut down gracefully.</summary>
sealed class PrimaryControlPipe(PrimarySettings settings, PrimaryRequests requests, IHostApplicationLifetime lifetime, ILogger<PrimaryControlPipe> log) : BackgroundService
{
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
      while(!cancel.IsCancellationRequested)
      {
         using var pipe = new NamedPipeServerStream(Name(settings.Port), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
         await pipe.WaitForConnectionAsync(cancel);
         try { await Answer(pipe, cancel); }
         catch(IOException error) { log.LogWarning("Could not deliver the local control result: {Reason}", error.Message); }
      }
   }

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
         else if(command.RootElement.ValueKind == JsonValueKind.Object && command.RootElement.TryGetProperty("command", out var verb) && verb.GetString() == "stop")
            reply = new(false, null, Stopping: true);
         else throw new ArgumentException("Unknown primary control command.");
      }
      catch(Exception error) when(!cancel.IsCancellationRequested) { reply = new(false, error.GetBaseException().Message); }
      await writer.WriteLineAsync(JsonSerializer.Serialize(reply));
      if(reply.Stopping) lifetime.StopApplication();
   }

   sealed record Reply(bool Removed, string? Error, bool Stopping = false);
}
