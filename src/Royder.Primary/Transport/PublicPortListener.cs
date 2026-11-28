using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;

namespace Royder.Primary.Transport;

/// <summary>
///   Kestrel's listener on the <see cref="PublicPort"/>: a thread of its own accepts the port's connections, and
///   Kestrel's socket transport serves each, as it serves those it accepts itself.
/// </summary>
/// <remarks>Kestrel's own socket transport accepts asynchronously, which a port that was handed over can't: see <see cref="PublicPort"/>.</remarks>
sealed class PublicPortListener(PublicPort port, ILoggerFactory logs) : IConnectionListenerFactory, IConnectionListenerFactorySelector, IConnectionListener
{
   readonly Channel<Socket> _accepted = Channel.CreateUnbounded<Socket>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
   readonly SocketConnectionContextFactory _connections = new(new SocketConnectionFactoryOptions(), logs.CreateLogger("Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets"));

   public EndPoint EndPoint => port.Address;

   public bool CanBind(EndPoint endpoint) => endpoint is PublicEndPoint;

   public ValueTask<IConnectionListener> BindAsync(EndPoint endpoint, CancellationToken cancel = default)
   {
      new Thread(Accepting) { IsBackground = true, Name = "Accepting on the public port" }.Start();
      return ValueTask.FromResult<IConnectionListener>(this);
   }

   void Accepting()
   {
      try
      {
         while(port.Accept() is { } connection)
         {
            connection.NoDelay = true;
            _accepted.Writer.TryWrite(connection);
         }
         _accepted.Writer.TryComplete();
      }
      // Kestrel's accept loop reports it.
      catch(Exception failure) { _accepted.Writer.TryComplete(failure); }
   }

   /// <summary>The next connection to serve: none once the port has been handed over, or closed as the primary stops.</summary>
   public async ValueTask<ConnectionContext?> AcceptAsync(CancellationToken cancel = default)
   {
      while(await _accepted.Reader.WaitToReadAsync(cancel))
         if(_accepted.Reader.TryRead(out var connection)) return _connections.Create(connection);
      return null;
   }

   public ValueTask UnbindAsync(CancellationToken cancel = default)
   {
      port.Close();
      return ValueTask.CompletedTask;
   }

   public ValueTask DisposeAsync()
   {
      port.Close();
      _connections.Dispose();
      return ValueTask.CompletedTask;
   }
}
