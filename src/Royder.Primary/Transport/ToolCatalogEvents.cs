using System.Collections.Concurrent;
using System.Threading.Channels;
using Newtonsoft.Json.Linq;

namespace Royder.Primary.Transport;

/// <summary>One catalog comparison for all event-stream subscribers, including when ordinary Rider starts or stops.</summary>
/// <remarks>
///   A stream ends as the primary stops, and one opened meanwhile ends at once. A graceful stop waits for the requests in
///   flight, and a stream ends only when its client hangs up: at an install on 2026-11-27 the stop waited out its three
///   minutes' timeout for the streams Claude Code sessions held, and port 23751 refused every connection meanwhile.
/// </remarks>
sealed class ToolCatalogEvents(PrimaryToolCatalog catalog, IHostApplicationLifetime lifetime, ILogger<ToolCatalogEvents> log) : BackgroundService
{
   readonly ConcurrentDictionary<Guid, Channel<long>> _subscribers = new();

   public async Task Stream(HttpResponse response, CancellationToken cancel)
   {
      response.ContentType = "text/event-stream";
      response.Headers.CacheControl = "no-cache";
      var id = Guid.NewGuid();
      var updates = Channel.CreateBounded<long>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
      _subscribers[id] = updates;
      using var open = CancellationTokenSource.CreateLinkedTokenSource(cancel, lifetime.ApplicationStopping);
      try
      {
         await Send(": connected\n\n");
         long delivered = -1;
         while(await updates.Reader.WaitToReadAsync(open.Token))
            if(updates.Reader.TryRead(out var revision))
            {
               await Send(revision != delivered
               ? "event: message\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n"
               : ": keep-alive\n\n");
               delivered = revision;
            }
      }
      catch(OperationCanceledException) when(lifetime.ApplicationStopping.IsCancellationRequested) { }
      finally { _subscribers.TryRemove(id, out _); }

      async Task Send(string message)
      {
         await response.WriteAsync(message, open.Token);
         await response.Body.FlushAsync(open.Token);
      }
   }

   protected override async Task ExecuteAsync(CancellationToken cancel)
   {
      JArray? published = null;
      long revision = 0;
      using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
      while(await timer.WaitForNextTickAsync(cancel))
      {
         if(_subscribers.IsEmpty) { published = null; continue; }
         try
         {
            var current = await catalog.Listed(cancel);
            var changed = published == null || !JToken.DeepEquals(current, published);
            if(changed) revision++;
            published = current;
            foreach(var channel in _subscribers.Values) channel.Writer.TryWrite(revision);
         }
         catch(Exception ex) when(!cancel.IsCancellationRequested) { log.LogWarning("Catalog refresh failed: {Reason}", ex.Message); }
      }
   }
}
