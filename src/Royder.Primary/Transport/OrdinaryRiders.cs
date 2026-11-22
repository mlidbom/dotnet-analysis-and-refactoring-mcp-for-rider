using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Newtonsoft.Json.Linq;
using Royder.Primary.Worktrees;

namespace Royder.Primary.Transport;

/// <summary>The ordinary Rider endpoint remains responsible for its own open solutions, editors and peer registrations.</summary>
sealed class OrdinaryRiders(HttpClient http, PrimarySettings settings)
{
   readonly RiderEndpoint? _endpoint = settings.DesktopPort is { } port ? new(http, port) : null;

   static readonly IPAddress[] AddressesServingLocalhost = [IPAddress.Loopback, IPAddress.Loopback.MapToIPv6(), IPAddress.Any, IPAddress.IPv6Any];

   /// <summary>A refused connection took two seconds on Windows. A fresh listener snapshot sees ordinary Rider open
   /// on the next request, including HTTP.sys listening on a wildcard address.</summary>
   RiderEndpoint? ListeningEndpoint =>
      _endpoint != null && IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                                             .Any(it => it.Port == _endpoint.Port && AddressesServingLocalhost.Contains(it.Address))
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
