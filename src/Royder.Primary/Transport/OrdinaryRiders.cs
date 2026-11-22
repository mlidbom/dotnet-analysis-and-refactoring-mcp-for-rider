using System.Net.Sockets;
using Newtonsoft.Json.Linq;
using Royder.Primary.Worktrees;

namespace Royder.Primary.Transport;

/// <summary>The ordinary Rider endpoint remains responsible for its own open solutions, editors and peer registrations.</summary>
sealed class OrdinaryRiders(HttpClient http, PrimarySettings settings)
{
   readonly RiderEndpoint? _endpoint = settings.DesktopPort is { } port ? new(http, port) : null;

   public async Task<ServingSolution[]> Solutions(CancellationToken cancel)
   {
      var rows = await Read(it => it.Listed(cancel)) ?? [];
      return rows.OfType<JObject>().Select(it => new ServingSolution((string)it["name"]!, (string)it["path"]!,
         (int)it["toolCount"]!, _endpoint!, false)).ToArray();
   }

   public Task<JObject?> Health(CancellationToken cancel) => _endpoint?.Health(cancel) ?? Task.FromResult<JObject?>(null);

   public Task<JArray?> Tools(CancellationToken cancel) => Read(it => it.Catalog(cancel));

   async Task<JArray?> Read(Func<RiderEndpoint, Task<JArray>> query)
   {
      if(_endpoint == null) return null;
      try { return await query(_endpoint); }
      catch(HttpRequestException ex) when(ex.GetBaseException() is SocketException { SocketErrorCode: SocketError.ConnectionRefused }) { return null; }
   }
}
