using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using DotNetAnalysisAndRefactoringMcp.Tools;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>
///   The peers whose port nothing listens on any more: their Rider ended without deregistering its solutions, as a
///   crashed or killed Rider does. The server forgets them before it routes a request.
/// </summary>
/// <remarks>
///   Until 2026-11-24 a peer was forgotten only when a call forwarded to it was refused. Its solutions stayed listed,
///   and every call that named no solution was refused with "Multiple solutions are open", as 73 specifications were in
///   one run while the peer sandbox's solution stayed registered.<br/>
///   The table of listening ports answers at once, where a refused connection takes two seconds on Windows. A port
///   HTTP.sys listens on for a Rider is listed on the wildcard address.
/// </remarks>
static class PeersThatStoppedListening
{
   static readonly ILogger Log = Logger.GetLogger(typeof(PeersThatStoppedListening));
   static readonly IPAddress[] AddressesServingLocalhost = [IPAddress.Loopback, IPAddress.IPv6Loopback, IPAddress.Any, IPAddress.IPv6Any];

   public static void Forget(IMcpServer server)
   {
      if(server.Peers.Count == 0) return;
      var listening = new HashSet<int>(IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
                                                         .Where(it => AddressesServingLocalhost.Contains(it.Address))
                                                         .Select(it => it.Port));
      foreach(var port in server.Peers.Select(it => it.Port).Distinct().Where(it => !listening.Contains(it)).ToList())
      {
         Log.Warn($"Forgetting the peer on port {port}: nothing listens on it.");
         server.RemovePeersOn(port);
      }
   }
}
