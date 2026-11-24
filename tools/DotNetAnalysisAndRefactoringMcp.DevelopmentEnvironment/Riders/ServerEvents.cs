using System.Collections.Concurrent;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

/// <summary>The lines of an open event stream, read as they come.</summary>
public sealed class ServerEvents : IDisposable
{
   static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

   readonly CancellationTokenSource _closing = new();
   readonly BlockingCollection<string> _lines = new();

   public ServerEvents(Uri address)
   {
      var response = Client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, _closing.Token).GetAwaiter().GetResult();
      response.EnsureSuccessStatusCode();
      var reader = new StreamReader(response.Content.ReadAsStream());
      Task.Run(() =>
      {
         try
         {
            while(reader.ReadLine() is {} line) _lines.Add(line);
         }
         catch(Exception) when(_closing.IsCancellationRequested) {}
         finally
         {
            response.Dispose();
         }
      });
   }

   /// <summary>Whether a line containing <paramref name="text"/> comes within <paramref name="timeout"/>.</summary>
   public bool Sends(string text, TimeSpan timeout)
   {
      var deadline = DateTime.UtcNow + timeout;
      while(DateTime.UtcNow < deadline)
         if(_lines.TryTake(out var line, deadline - DateTime.UtcNow) && line.Contains(text, StringComparison.Ordinal))
            return true;
      return false;
   }

   public void Dispose() => _closing.Cancel();
}
