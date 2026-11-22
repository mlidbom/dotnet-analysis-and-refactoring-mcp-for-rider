#:property TargetFramework=net10.0
using System.Net;
using System.Net.Sockets;
using System.Text;

// Use the same transport and loopback endpoint as OtherMcpServer.Send after the private peer exits.
#pragma warning disable SYSLIB0014
var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:23921/");
#pragma warning restore SYSLIB0014
request.Method = "POST";
request.ContentType = "application/json";
request.Timeout = 5000;
var body = Encoding.UTF8.GetBytes("""{"jsonrpc":"2.0","id":1,"method":"internal/status","params":{}}""");
request.ContentLength = body.Length;
try
{
   using(var stream = request.GetRequestStream()) stream.Write(body);
   using var response = request.GetResponse();
   throw new InvalidOperationException("The stopped peer's port unexpectedly answered.");
}
catch(WebException exception)
{
   var socket = exception.InnerException?.InnerException as SocketException;
   Console.WriteLine($"CLR {Environment.Version}, WebException.Status={exception.Status}, " +
                     $"SocketErrorCode={socket?.SocketErrorCode}, NativeErrorCode={socket?.NativeErrorCode}");
}
