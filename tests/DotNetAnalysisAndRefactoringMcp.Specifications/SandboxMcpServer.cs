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

  /// <summary>Calls <paramref name="tool"/> and returns the text it answers.</summary>
  /// <remarks>
  ///   A tool that can't answer, such as for a symbol that doesn't exist, still answers in text, as
  ///   <c>{"error": ...}</c>: that is upstream's convention. Only a call the server itself rejects throws.
  /// </remarks>
  public string CallTool(string tool, object arguments)
  {
    var request = new JsonObject
    {
      ["jsonrpc"] = "2.0",
      ["id"] = Interlocked.Increment(ref _lastRequestId),
      ["method"] = "tools/call",
      ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = JsonSerializer.SerializeToNode(arguments) }
    };

    using var response = Client.PostAsJsonAsync(_address, request).GetAwaiter().GetResult();
    response.EnsureSuccessStatusCode();
    using var answer = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
    if (answer.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException($"The server rejected the call of {tool}: {error}");

    var result = answer.RootElement.GetProperty("result");
    var text = string.Join("\n", result.GetProperty("content").EnumerateArray().Select(it => it.GetProperty("text").GetString()));
    if (result.TryGetProperty("isError", out var isError) && isError.GetBoolean()) throw new InvalidOperationException($"{tool} failed: {text}");
    return text;
  }
}
