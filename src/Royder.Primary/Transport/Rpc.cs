using Newtonsoft.Json.Linq;

namespace Royder.Primary.Transport;

/// <summary>The JSON-RPC envelopes and MCP text answers shared by the public endpoint and its Rider connections.</summary>
static class Rpc
{
   public static JObject Request(string method, JObject? parameters = null) =>
      new() { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = method, ["params"] = parameters ?? new() };

   public static JObject Result(JToken id, JToken result) =>
      new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

   public static JObject Error(JToken? id, int code, string message) =>
      new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JObject { ["code"] = code, ["message"] = message } };

   public static JObject ToolResult(JToken id, string text, bool failed = false) =>
      Result(id, new JObject { ["content"] = new JArray(new JObject { ["type"] = "text", ["text"] = text }), ["isError"] = failed });

   public static string TextOf(JObject reply) =>
      (string?)reply.SelectToken("result.content[0].text") ?? throw new InvalidDataException($"Invalid MCP answer: {reply["error"]}");
}
