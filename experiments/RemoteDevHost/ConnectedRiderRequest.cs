using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RemoteDevHost;

/// <summary>Posts on the exact established loopback socket whose remote endpoint the supervisor verified belongs to its JVM.</summary>
public static class ConnectedRiderRequest
{
   public sealed class Reply
   {
      public int Status { get; init; }
      public string ETag { get; init; }
      public string Message { get; init; }
   }

   public static async Task<Reply> PostAsync(TcpClient connection, Uri endpoint, string token, string solution, string ifMatch, string openId)
   {
      using var stream = connection.GetStream();
      using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
      var supplied = false;
      handler.ConnectCallback = (_, _) =>
      {
         if(supplied) throw new InvalidOperationException("The verified Rider connection closed.");
         supplied = true;
         return ValueTask.FromResult<System.IO.Stream>(stream);
      };
      using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
      using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
      if(!string.IsNullOrEmpty(openId)) request.Headers.Add("X-Royder-Open-Id", openId);
      if(!string.IsNullOrEmpty(ifMatch)) request.Headers.IfMatch.Add(EntityTagHeaderValue.Parse(ifMatch));
      request.Content = new StringContent(solution, Encoding.UTF8, "text/plain");
      using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
      var tag = response.Headers.ETag;
      var message = "";
      if(!response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType == "text/plain")
      {
         using var reader = new System.IO.StreamReader(await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
         var buffer = new char[1024];
         int count = await reader.ReadBlockAsync(buffer,0,buffer.Length).ConfigureAwait(false);
         message = new string(buffer,0,count).Replace(token,"[redacted]",StringComparison.Ordinal);
      }
      return new Reply { Status = (int)response.StatusCode, ETag = tag is { IsWeak: false } && tag.Tag != "*" ? tag.Tag : null, Message = message };
   }
}
