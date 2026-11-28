using System.Net;
using System.Net.Sockets;

namespace Royder.Primary.Transport;

/// <summary>
///   The <see cref="PublicPort"/>'s address as Kestrel is told to listen on it: of a type of its own, which Kestrel's
///   own socket transport leaves to <see cref="PublicPortListener"/>. That transport would bind the address anew.
/// </summary>
sealed class PublicEndPoint(IPEndPoint address) : EndPoint
{
   public override AddressFamily AddressFamily => address.AddressFamily;

   public override string ToString() => address.ToString();
}
