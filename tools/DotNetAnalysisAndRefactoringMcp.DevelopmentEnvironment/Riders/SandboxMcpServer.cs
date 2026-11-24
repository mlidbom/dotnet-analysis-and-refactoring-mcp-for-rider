using System.Text.Json;
using System.Text.Json.Nodes;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

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
   public JsonElement ToolsOf(string solution) =>
      Health().GetProperty("solutions").EnumerateArray()
              .Single(it => string.Equals(it.GetProperty("path").GetString(), solution, StringComparison.OrdinalIgnoreCase))
              .GetProperty("tools");

   /// <summary>What the <c>/health</c> endpoint answers: the open solutions, whether any is indexing, the tools builds.</summary>
   public JsonElement Health()
   {
      using var health = JsonDocument.Parse(Client.GetStringAsync(new Uri(_address, "health")).GetAwaiter().GetResult());
      return health.RootElement.Clone();
   }

   /// <summary>What <c>internal/status</c> answers, the request the Riders' servers and the standalone primary send each other.</summary>
   public JsonElement Status() => Send("internal/status", new JsonObject());

   /// <summary>The result of the <c>initialize</c> request a client sends when it connects, in <paramref name="protocolVersion"/>.</summary>
   public JsonElement Initialize(string protocolVersion = "2025-03-26") =>
      Send("initialize", new JsonObject { ["protocolVersion"] = protocolVersion, ["capabilities"] = new JsonObject() });

   /// <summary>What <c>list_solutions</c> answers: every open solution's name, path, tool count, and the path segment that picks it out.</summary>
   public JsonElement ListSolutions()
   {
      using var listed = JsonDocument.Parse(CallTool("list_solutions", new {}));
      return listed.RootElement.Clone();
   }

   /// <summary>Register a solution with the primary as a peer does when it opens the solution.</summary>
   public void RegisterPeer(string solutionPath, int port) =>
      Send("internal/register", new JsonObject { ["solutionName"] = Path.GetFileNameWithoutExtension(solutionPath),
                                                ["solutionPath"] = solutionPath, ["port"] = port, ["tools"] = new JsonArray() });

   /// <summary>Withdraw a peer's solution from the primary when the specification ends.</summary>
   public void DeregisterPeer(string solutionPath) => Send("internal/deregister", new JsonObject { ["solutionPath"] = solutionPath });

   /// <summary>The path segment <c>list_solutions</c> gives the solution at <paramref name="solutionPath"/>, to name it by.</summary>
   public string UniquePathSegmentOf(string solutionPath) =>
      ListSolutions().GetProperty("solutions").EnumerateArray()
                     .Single(it => string.Equals(it.GetProperty("path").GetString(), solutionPath, StringComparison.OrdinalIgnoreCase))
                     .GetProperty("uniquePathSegment").GetString()!;

   /// <summary>The tools <c>tools/list</c> lists, as their definitions.</summary>
   public IReadOnlyList<JsonElement> ListTools() => Send("tools/list", new JsonObject()).GetProperty("tools").EnumerateArray().ToList();

   /// <summary>An event stream, opened as a client opens one with a GET: what the server sends without being asked.</summary>
   public ServerEvents OpenEventStream() => new(_address);

   /// <summary>Calls <paramref name="tool"/> and returns the text it answers.</summary>
   /// <remarks>
   ///   A tool that can't answer, such as for a symbol that doesn't exist, still answers in text, as
   ///   <c>{"error": ...}</c>: that is upstream's convention. Only a call the server itself rejects throws.
   /// </remarks>
   public string CallTool(string tool, object arguments)
   {
      var (text, isError) = Call(tool, arguments);
      if(isError) throw new InvalidOperationException($"{tool} failed: {text}");
      return text;
   }

   /// <summary>A tool's answer in its JSON form, which the call's arguments ask for with format: "json", parsed.</summary>
   public JsonNode Json(string tool, object arguments) => JsonNode.Parse(CallTool(tool, arguments))!;

   /// <summary>Calls <paramref name="tool"/> as the server is to refuse, and returns the refusal's text.</summary>
   public string Refusal(string tool, object arguments)
   {
      var (text, isError) = Call(tool, arguments);
      if(!isError) throw new InvalidOperationException($"{tool} answered instead of refusing: {text}");
      return text;
   }

   /// <summary>The JSON-RPC error the server answers <paramref name="method"/> with.</summary>
   public JsonElement Error(string method)
   {
      var answer = Post(Request(method, new JsonObject()));
      if(!answer.TryGetProperty("error", out var error)) throw new InvalidOperationException($"The server answered {method}: {answer}");
      return error;
   }

   /// <summary>Sends <paramref name="method"/> as a notification, a request without an id, and returns the HTTP status and body it gets.</summary>
   public (int Status, string Body) Notify(string method)
   {
      var notification = Request(method, new JsonObject());
      notification.Remove("id");
      using var response = Client.PostAsJsonAsync(_address, notification).GetAwaiter().GetResult();
      return ((int)response.StatusCode, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
   }

   (string Text, bool IsError) Call(string tool, object arguments)
   {
      var result = Send("tools/call", new JsonObject { ["name"] = tool, ["arguments"] = JsonSerializer.SerializeToNode(arguments) });
      var text = string.Join("\n", result.GetProperty("content").EnumerateArray().Select(it => it.GetProperty("text").GetString()));
      return (text, result.TryGetProperty("isError", out var isError) && isError.GetBoolean());
   }

   JsonElement Send(string method, JsonObject parameters)
   {
      var answer = Post(Request(method, parameters));
      if(answer.TryGetProperty("error", out var error)) throw new InvalidOperationException($"The server rejected {method}: {error}");
      return answer.GetProperty("result");
   }

   JsonObject Request(string method, JsonObject parameters) =>
      new()
      {
         ["jsonrpc"] = "2.0",
         ["id"] = Interlocked.Increment(ref _lastRequestId),
         ["method"] = method,
         ["params"] = parameters
      };

   JsonElement Post(JsonObject request)
   {
      using var response = Client.PostAsJsonAsync(_address, request).GetAwaiter().GetResult();
      response.EnsureSuccessStatusCode();
      using var answer = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
      return answer.RootElement.Clone();
   }
}
