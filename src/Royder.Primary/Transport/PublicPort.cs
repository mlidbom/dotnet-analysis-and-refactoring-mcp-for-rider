using System.Net;
using System.Net.Sockets;

namespace Royder.Primary.Transport;

/// <summary>
///   The primary's public port, such as 127.0.0.1:23751, as its MCP clients know it: the socket listening on it, and the
///   MCP session the clients hold. At an install a primary hands both to its successor, so that the port refuses no
///   connection and the clients keep their session. <see cref="PublicPortListener"/> serves the port's connections.
/// </summary>
/// <remarks>
///   Until 2026-11-28 an install stopped the primary before its successor bound the port, which refused connections
///   meanwhile: for about 4 seconds, and for 3 minutes before b06ba6a. Claude Code drops a server's tools at a refused
///   connection, until /mcp or a tool search connects it again. During a hand-over a connection waits in the socket's
///   queue until the successor accepts it.
/// </remarks>
/// <remarks>
///   Windows shares a socket with another process as a duplicate of it, <see cref="Socket.DuplicateAndClose"/>. An
///   asynchronous accept binds the socket to its process's I/O completion port, which the duplicate shares and can't
///   leave: the successor's asynchronous accept failed with "'handle' has already been bound to the thread pool". So no
///   primary accepts asynchronously on the port: <see cref="Accept"/> waits for a connection.<br/>
///   Windows' TCP table goes on naming the process that bound the port first, also once it has handed the port over and
///   ended: the primary's health names its own process.
/// </remarks>
sealed class PublicPort
{
   /// <summary>
   ///   How long a client takes to close a connection it no longer uses, which it holds open for its next request: the
   ///   time a primary goes on serving the connections it accepted, once it has handed the port over.
   /// </summary>
   /// <remarks>
   ///   A primary's stop closes the idle connections, and a request a client sends on one meanwhile fails. Undici,
   ///   Claude Code's HTTP client, closes a connection that has been idle for 4 seconds. Answers that the primary gives
   ///   after the hand-over close their connections (<see cref="HasBeenHandedOver"/>).
   /// </remarks>
   public static readonly TimeSpan TimeForClientsToLetGo = TimeSpan.FromSeconds(5);

   const int Listening = 0, HandedOver = 1, Closed = 2;
   readonly Socket _listening;
   int _state = Listening;

   PublicPort(Socket listening, string session, PreviousPrimary predecessor)
   {
      _listening = listening;
      Session = session;
      Predecessor = predecessor;
   }

   /// <summary>The MCP session the port's clients hold, by the <c>Mcp-Session-Id</c> header: the same before and after a hand-over.</summary>
   public string Session { get; }

   /// <summary>The primary that handed this one the port, or <see cref="PreviousPrimary.None"/>.</summary>
   public PreviousPrimary Predecessor { get; }

   public IPEndPoint Address => (IPEndPoint)_listening.LocalEndPoint!;

   /// <summary>Whether a successor serves the port: this primary serves only the connections it accepted before.</summary>
   public bool HasBeenHandedOver => Volatile.Read(ref _state) == HandedOver;

   /// <summary>The port <paramref name="number"/> on the loopback address: bound by this primary, or taken over from the primary on it.</summary>
   public static async Task<PublicPort> Open(int number)
   {
      var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
      try
      {
         socket.Bind(new IPEndPoint(IPAddress.Loopback, number));
         socket.Listen(512);
         return new(socket, Guid.NewGuid().ToString("N"), PreviousPrimary.None);
      }
      catch(SocketException taken) when(taken.SocketErrorCode == SocketError.AddressAlreadyInUse)
      {
         socket.Dispose();
         var handOver = await PrimaryControlPipe.TakeOver(number);
         return new(handOver.ListeningSocket(), handOver.Session, PreviousPrimary.Of(handOver.ProcessId));
      }
   }

   /// <summary>
   ///   Hands the port to the primary in the process <paramref name="successor"/>, which accepts its connections from
   ///   now on.
   /// </summary>
   public PortHandOver HandOverTo(int successor)
   {
      if(Interlocked.CompareExchange(ref _state, HandedOver, Listening) != Listening)
         throw new InvalidOperationException("This primary has already handed its port over, or is stopping.");
      SocketInformation duplicate;
      try { duplicate = _listening.DuplicateAndClose(successor); }
      catch { Volatile.Write(ref _state, Listening); throw; }
      return new(Convert.ToBase64String(duplicate.ProtocolInformation), (int)duplicate.Options, Session, Environment.ProcessId);
   }

   /// <summary>The next connection a client makes, once it has: none once the port has been handed over or closed.</summary>
   public Socket? Accept()
   {
      while(true)
         try { return _listening.Accept(); }
         // A client that gave up before its connection was accepted: Kestrel's own socket transport goes on too.
         catch(SocketException reset) when(reset.SocketErrorCode == SocketError.ConnectionReset) { }
         // Handing the port over or closing it ends the accept that waited.
         catch(Exception) when(Volatile.Read(ref _state) != Listening) { return null; }
   }

   /// <summary>Stops listening, as the primary stops, unless it has handed the port over.</summary>
   public void Close()
   {
      if(Interlocked.CompareExchange(ref _state, Closed, Listening) == Listening) _listening.Dispose();
   }
}
