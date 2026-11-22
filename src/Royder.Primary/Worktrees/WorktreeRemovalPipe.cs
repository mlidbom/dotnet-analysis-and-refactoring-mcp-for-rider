using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Royder.Primary.Worktrees;

/// <summary>The local remove-worktree command asks the primary in this user's Windows login to close and remove the tree.</summary>
sealed class WorktreeRemovalPipe(PrimarySettings settings, Transport.PrimaryRequests requests, ILogger<WorktreeRemovalPipe> log) : BackgroundService
{
   static string Name(int port) => $"Royder.Primary.RemoveWorktree.{port}";
   static readonly Encoding Utf8 = new UTF8Encoding(false, true);

   public static async Task Remove(int port, string path)
   {
      using var pipe = new NamedPipeClientStream(".", Name(port), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
      await pipe.ConnectAsync(5000);
      using var writer = new StreamWriter(pipe, Utf8, leaveOpen: true) { AutoFlush = true };
      using var reader = new StreamReader(pipe, Utf8, leaveOpen: true);
      await writer.WriteLineAsync(JsonSerializer.Serialize(Path.GetFullPath(path)));
      var reply = JsonSerializer.Deserialize<RemovalReply>(await reader.ReadLineAsync() ?? throw new IOException("The primary closed its removal connection."));
      if(reply == null || !reply.Removed) throw new InvalidOperationException(reply?.Error ?? "Missing removal confirmation.");
   }

   protected override async Task ExecuteAsync(CancellationToken cancel)
   {
      while(!cancel.IsCancellationRequested)
      {
         using var pipe = new NamedPipeServerStream(Name(settings.Port), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
         await pipe.WaitForConnectionAsync(cancel);
         try { await Answer(pipe, cancel); }
         catch(IOException error) { log.LogWarning("Could not deliver the local removal result: {Reason}", error.Message); }
      }
   }

   async Task Answer(NamedPipeServerStream pipe, CancellationToken cancel)
   {
      using var reader = new StreamReader(pipe, Utf8, leaveOpen: true);
      using var writer = new StreamWriter(pipe, Utf8, leaveOpen: true) { AutoFlush = true };
      RemovalReply reply;
      try
      {
         using var input = CancellationTokenSource.CreateLinkedTokenSource(cancel);
         input.CancelAfter(TimeSpan.FromSeconds(5));
         var path = JsonSerializer.Deserialize<string>(await reader.ReadLineAsync(input.Token) ?? "");
         await requests.Remove(path ?? "", cancel);
         reply = new(true, null);
      }
      catch(Exception error) when(!cancel.IsCancellationRequested) { reply = new(false, error.GetBaseException().Message); }
      await writer.WriteLineAsync(JsonSerializer.Serialize(reply));
   }

   sealed record RemovalReply(bool Removed, string? Error);
}
