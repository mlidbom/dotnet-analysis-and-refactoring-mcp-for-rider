using System;
using System.IO;
using System.Net;
using System.Text;
using DotNetAnalysisAndRefactoringMcp.Protocol;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>The MCP server of another Rider on this machine, reached at its port: a peer of this one, or the primary.</summary>
sealed class OtherMcpServer(int port)
{
   public int Port { get; } = port;

   /// <summary>Its answer to <paramref name="request"/>, as JSON text; a <see cref="WebException"/> when it doesn't answer.</summary>
   public string Send(JsonRpcRequest request, TimeSpan timeout)
   {
      var webRequest = (HttpWebRequest)WebRequest.Create($"http://127.0.0.1:{Port}/");
      webRequest.Method = "POST";
      webRequest.ContentType = "application/json";
      webRequest.Timeout = (int)timeout.TotalMilliseconds;

      var bytes = Encoding.UTF8.GetBytes(JsonOf.Value(request).ToString(Newtonsoft.Json.Formatting.None));
      webRequest.ContentLength = bytes.Length;
      using(var stream = webRequest.GetRequestStream())
         stream.Write(bytes, 0, bytes.Length);

      using var response = (HttpWebResponse)webRequest.GetResponse();
      using var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
      return reader.ReadToEnd();
   }
}
