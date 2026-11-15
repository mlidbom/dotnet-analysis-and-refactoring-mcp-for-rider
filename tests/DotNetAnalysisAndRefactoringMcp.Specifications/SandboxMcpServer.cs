using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotNetAnalysisAndRefactoringMcp.Specifications;

/// <summary>The plugin's MCP server in the sandbox Rider, called as an MCP client calls it: JSON-RPC over HTTP.</summary>
public sealed class SandboxMcpServer
{
   static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(2) };

   readonly Uri _address;
   int _lastRequestId;

   public SandboxMcpServer(int port) => _address = new Uri($"http://127.0.0.1:{port}/");

   /// <summary>
   ///   What the <c>/health</c> endpoint says about the tools build that serves <paramref name="solution"/>: its name as
   ///   <c>build</c>, and the names of the builds swapped out that are <c>beingFreed</c> or <c>notFreed</c>.
   /// </summary>
   public JsonElement ToolsOf(string solution)
   {
      using var health = JsonDocument.Parse(Client.GetStringAsync(new Uri(_address, "health")).GetAwaiter().GetResult());
      return health.RootElement.GetProperty("solutions").EnumerateArray()
                   .Single(it => string.Equals(it.GetProperty("path").GetString(), solution, StringComparison.OrdinalIgnoreCase))
                   .GetProperty("tools").Clone();
   }

   /// <summary>The result of the <c>initialize</c> request a client sends when it connects.</summary>
   public JsonElement Initialize() =>
      Send("initialize", new JsonObject { ["protocolVersion"] = "2025-03-26", ["capabilities"] = new JsonObject() });

   /// <summary>An event stream, opened as a client opens one with a GET: what the server sends without being asked.</summary>
   public ServerEvents OpenEventStream() => new(_address);

   /// <summary>Calls <paramref name="tool"/> and returns the text it answers.</summary>
   /// <remarks>
   ///   A tool that can't answer, such as for a symbol that doesn't exist, still answers in text, as
   ///   <c>{"error": ...}</c>: that is upstream's convention. Only a call the server itself rejects throws.
   /// </remarks>
   public string CallTool(string tool, object arguments)
   {
      var result = Send("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = JsonSerializer.SerializeToNode(arguments) });
      var text = string.Join("\n", result.GetProperty("content").EnumerateArray().Select(it => it.GetProperty("text").GetString()));
      if(result.TryGetProperty("isError", out var isError) && isError.GetBoolean()) throw new InvalidOperationException($"{tool} failed: {text}");
      return text;
   }

   JsonElement Send(string method, JsonObject parameters)
   {
      var request = new JsonObject
                    {
                       ["jsonrpc"] = "2.0",
                       ["id"] = Interlocked.Increment(ref _lastRequestId),
                       ["method"] = method,
                       ["params"] = parameters
                    };

      using var response = Client.PostAsJsonAsync(_address, request).GetAwaiter().GetResult();
      response.EnsureSuccessStatusCode();
      using var answer = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
      if(answer.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException($"The server rejected {method}: {error}");
      return answer.RootElement.GetProperty("result").Clone();
   }
}

/// <summary>The lines of an open event stream, read as they come.</summary>
public sealed class ServerEvents : IDisposable
{
   static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };

   readonly CancellationTokenSource _closing = new();
   readonly BlockingCollection<string> _lines = new();

   public ServerEvents(Uri address)
   {
      var response = Client.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, _closing.Token).GetAwaiter().GetResult();
      response.EnsureSuccessStatusCode();
      var reader = new StreamReader(response.Content.ReadAsStream());
      Task.Run(() =>
      {
         try
         {
            while(reader.ReadLine() is {} line) _lines.Add(line);
         }
         catch(Exception) when(_closing.IsCancellationRequested) {}
         finally
         {
            response.Dispose();
         }
      });
   }

   /// <summary>Whether a line containing <paramref name="text"/> comes within <paramref name="timeout"/>.</summary>
   public bool Sends(string text, TimeSpan timeout)
   {
      var deadline = DateTime.UtcNow + timeout;
      while(DateTime.UtcNow < deadline)
         if(_lines.TryTake(out var line, deadline - DateTime.UtcNow) && line.Contains(text, StringComparison.Ordinal))
            return true;
      return false;
   }

   public void Dispose() => _closing.Cancel();
}
