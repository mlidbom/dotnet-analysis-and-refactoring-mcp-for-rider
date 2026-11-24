using System.Net.Sockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Royder.Primary.Transport;

/// <summary>The ordinary Royder protocol at a known loopback port, including the peers that endpoint routes to.</summary>
sealed class RiderEndpoint(HttpClient http, int port)
{
   readonly Uri _address = new($"http://127.0.0.1:{port}/");
   public int Port { get; } = port;

   public async Task<JObject> Send(JObject request, CancellationToken cancellation)
   {
      using var body = new StringContent(request.ToString(Formatting.None), Encoding.UTF8, "application/json");
      using var response = await http.PostAsync(_address, body, cancellation);
      response.EnsureSuccessStatusCode();
      return JObject.Parse(await response.Content.ReadAsStringAsync(cancellation));
   }

   /// <summary>The endpoint's <c>/health</c>; null while nothing there answers, such as while a peer takes over its port.</summary>
   /// <remarks>
   ///   A connection reset counts as nothing answering too. On 2026-11-24, with the machine's CPU at 100%, one failed the
   ///   primary's own <c>/health</c> with a 500, which ended an update of the primary; and the warm-up polls this, which it
   ///   would have failed.
   /// </remarks>
   public async Task<JObject?> Health(CancellationToken cancellation)
   {
      try { return JObject.Parse(await http.GetStringAsync(new Uri(_address, "health"), cancellation)); }
      catch(HttpRequestException ex) when(ex.GetBaseException() is SocketException { SocketErrorCode: SocketError.ConnectionRefused or SocketError.ConnectionReset }) { return null; }
   }

   public async Task<JArray> Catalog(CancellationToken cancellation)
   {
      var reply = await Send(Rpc.Request("tools/list"), cancellation);
      return reply.SelectToken("result.tools") as JArray ?? throw new InvalidDataException($"Royder on port {Port} supplied no tool catalog.");
   }

   public async Task<JArray> Listed(CancellationToken cancellation)
   {
      var reply = await Send(Rpc.Request("tools/call", new JObject { ["name"] = "list_solutions" }), cancellation);
      return JObject.Parse(Rpc.TextOf(reply))["solutions"] as JArray ?? throw new InvalidDataException("Missing list of Rider solutions.");
   }
}
