using System.Net.Sockets;

namespace Royder.Primary.Transport;

/// <summary>
///   What a primary hands its successor over the control pipe with its <see cref="PublicPort"/>: the duplicate of the
///   socket listening on it, made for the successor's process, the MCP session the port's clients hold, and the process
///   of the primary that hands it over.
/// </summary>
/// <param name="ProtocolInformation">The duplicate's <see cref="SocketInformation.ProtocolInformation"/>, in Base64.</param>
/// <param name="Options">The duplicate's <see cref="SocketInformation.Options"/>.</param>
sealed record PortHandOver(string ProtocolInformation, int Options, string Session, int ProcessId)
{
   public Socket ListeningSocket() =>
      new(new SocketInformation { ProtocolInformation = Convert.FromBase64String(ProtocolInformation), Options = (SocketInformationOptions)Options });
}
