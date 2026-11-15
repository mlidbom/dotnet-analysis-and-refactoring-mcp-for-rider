using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Protocol;

/// <summary>
///   A JSON-RPC 2.0 request: what an MCP client posts to the server, and what one Rider's server posts to another's.
/// </summary>
/// <remarks>
///   Read with <see cref="Parse"/> and written with <see cref="JsonOf"/>, never with Newtonsoft.Json's serializer:
///   it would keep this tools build loaded after it was swapped out (see <see cref="JsonOf"/>).
/// </remarks>
public class JsonRpcRequest
{
   [JsonProperty("jsonrpc")]
   public string JsonRpc { get; set; } = "2.0";

   /// <summary>The request's id, which its response repeats; <c>null</c> for a notification, which gets no response.</summary>
   [JsonProperty("id")]
   public object Id { get; set; }

   [JsonProperty("method")]
   public string Method { get; set; }

   [JsonProperty("params")]
   public JObject Params { get; set; }

   /// <summary>Whether it is a notification: a request without an id, which is answered with no response.</summary>
   public bool IsNotification => Id == null && Method != null;

   /// <summary>The request <paramref name="json"/> is the text of.</summary>
   public static JsonRpcRequest Parse(string json)
   {
      var request = JObject.Parse(json);

      return new JsonRpcRequest
             {
                Id = request["id"] is { Type: not JTokenType.Null } id ? id : null,
                Method = request["method"]?.ToString(),
                Params = request["params"] as JObject
             };
   }

   /// <summary>The response that answers it with <paramref name="result"/>.</summary>
   public JsonRpcResponse Answer(object result) => new() { Id = Id, Result = result };

   /// <summary>The response that answers it with an error of JSON-RPC's.</summary>
   public JsonRpcResponse Error(int code, string message) => new() { Id = Id, Error = new JsonRpcError { Code = code, Message = message } };

   /// <summary>The response to a <c>tools/call</c> that a tool answered with <paramref name="text"/>.</summary>
   public JsonRpcResponse ToolAnswer(string text) => Answer(new CallToolResult { Content = { new ContentBlock { Text = text } } });

   /// <summary>The response to a <c>tools/call</c> that no tool could answer, saying why.</summary>
   public JsonRpcResponse ToolError(string message) => Answer(new CallToolResult { IsError = true, Content = { new ContentBlock { Text = message } } });
}

public class JsonRpcResponse
{
   [JsonProperty("jsonrpc")]
   public string JsonRpc { get; set; } = "2.0";

   [JsonProperty("id")]
   public object Id { get; set; }

   [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
   public object Result { get; set; }

   [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
   public JsonRpcError Error { get; set; }

   /// <summary>The response <paramref name="json"/> is the text of, as another Rider's server answered.</summary>
   public static JsonRpcResponse Parse(string json)
   {
      var response = JObject.Parse(json);

      return new JsonRpcResponse
             {
                Id = response["id"],
                Result = response["result"],
                Error = response["error"] is JObject error
                           ? new JsonRpcError { Code = error["code"]?.Value<int>() ?? 0, Message = error["message"]?.ToString(), Data = error["data"] }
                           : null
             };
   }

   /// <summary>Its JSON text, as the server answers it.</summary>
   public string Json() => JsonOf.Value(this).ToString(Formatting.None);
}

public class JsonRpcError
{
   [JsonProperty("code")]
   public int Code { get; set; }

   [JsonProperty("message")]
   public string Message { get; set; }

   [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
   public object Data { get; set; }
}
