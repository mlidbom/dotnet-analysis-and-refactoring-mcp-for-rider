using System.Collections.Concurrent;
using System.Threading.Channels;
using Newtonsoft.Json.Linq;

namespace Royder.Primary.Transport;

/// <summary>One catalog comparison for all event-stream subscribers, including when ordinary Rider starts or stops.</summary>
sealed class ToolCatalogEvents(PrimaryToolCatalog catalog, ILogger<ToolCatalogEvents> log) : BackgroundService
{
   readonly ConcurrentDictionary<Guid, Channel<long>> _subscribers = new();

   public async Task Stream(HttpResponse response, CancellationToken cancel)
   {
      response.ContentType = "text/event-stream";
      response.Headers.CacheControl = "no-cache";
      var id = Guid.NewGuid();
      var updates = Channel.CreateBounded<long>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
      _subscribers[id] = updates;
      try
      {
         await Send(": connected\n\n");
         long delivered = -1;
         while(await updates.Reader.WaitToReadAsync(cancel))
            if(updates.Reader.TryRead(out var revision))
            {
               await Send(revision != delivered
               ? "event: message\ndata: {\"jsonrpc\":\"2.0\",\"method\":\"notifications/tools/list_changed\"}\n\n"
               : ": keep-alive\n\n");
               delivered = revision;
            }
      }
      finally { _subscribers.TryRemove(id, out _); }

      async Task Send(string message)
      {
         await response.WriteAsync(message, cancel);
         await response.Body.FlushAsync(cancel);
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
