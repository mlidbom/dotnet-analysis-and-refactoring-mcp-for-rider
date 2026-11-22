using System.Net.NetworkInformation;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;
using Royder.Primary.Worktrees;

namespace Royder.Primary.Transport;

/// <summary>The ordinary Rider endpoint remains responsible for its own open solutions, editors and peer registrations.</summary>
sealed class OrdinaryRiders(HttpClient http, PrimarySettings settings)
{
   readonly RiderEndpoint? _endpoint = settings.DesktopPort is { } port ? new(http, port) : null;

   // A refused loopback connection took two seconds on Windows. A fresh OS snapshot sees a newly opened
   // Rider on the next request, including an HTTP.sys listener owned by the kernel.
   RiderEndpoint? ListeningEndpoint =>
      _endpoint != null && IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(it => it.Port == _endpoint.Port)
         ? _endpoint : null;

   public async Task<ServingSolution[]> Solutions(CancellationToken cancel)
   {
      var rows = await Read(it => it.Listed(cancel)) ?? [];
      return rows.OfType<JObject>().Select(it => new ServingSolution((string)it["name"]!, (string)it["path"]!,
         (int)it["toolCount"]!, _endpoint!, false)).ToArray();
   }

   public Task<JObject?> Health(CancellationToken cancel) => ListeningEndpoint?.Health(cancel) ?? Task.FromResult<JObject?>(null);

   public Task<JArray?> Tools(CancellationToken cancel) => Read(it => it.Catalog(cancel));

   async Task<JArray?> Read(Func<RiderEndpoint, Task<JArray>> query)
   {
      var endpoint = ListeningEndpoint;
      if(endpoint == null) return null;
      try { return await query(endpoint); }
      catch(HttpRequestException ex) when(ex.GetBaseException() is SocketException { SocketErrorCode: SocketError.ConnectionRefused }) { return null; }
   }
}
