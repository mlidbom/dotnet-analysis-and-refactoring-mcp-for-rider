#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using JetBrains.Application;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   One tool call over many items (<c>filePaths</c>, <c>queries</c>, <c>symbols</c>, ...), answered under
///   <c>=== [N/total] label ===</c> headers.
/// </summary>
/// <remarks>
///   Every top-level argument applies to every item, except the ones that name an item. Each item runs on its
///   own: one that throws reports a one-line failure under its header, and the rest of the batch still answers.
/// </remarks>
public sealed class ToolBatch
{
   static readonly string[] SymbolKeys = { "symbolName", "kind", "filePath", "line", "column" };

   readonly JObject _arguments;
   readonly JArray _items;
   readonly HashSet<string> _naming;
   readonly string _item;

   ToolBatch(JObject arguments, JArray items, string itemsKey, string item)
   {
      _arguments = arguments;
      _items = items;
      _item = item;
      _naming = new HashSet<string>(SymbolKeys) { itemsKey, item };
   }

   /// <param name="items">The array argument, e.g. <c>filePaths</c>.</param>
   /// <param name="item">The single-item argument a string item becomes, e.g. <c>filePath</c>.</param>
   /// <returns><c>null</c> when the call is not a batch.</returns>
   public static ToolBatch? Of(JObject arguments, string items, string item) =>
      arguments[items] is JArray array && array.Count > 0
         ? new ToolBatch(arguments, array, items, item)
         : null;

   public string Run(Func<JObject, object> single)
   {
      var text = new StringBuilder();
      for(var index = 0; index < _items.Count; index++)
      {
         var item = _items[index];
         if(index > 0)
            text.Append('\n').Append('\n');

         text.Append($"=== [{index + 1}/{_items.Count}] {Label(item)} ===").Append('\n');
         text.Append(ToolText.Of(Isolated(single, Arguments(item), Label(item))));
      }

      return text.ToString().TrimEnd();
   }

   static object Isolated(Func<JObject, object> single, JObject arguments, string label)
   {
      try
      {
         return single(arguments);
      }
      catch(Exception exception) when(exception is not OperationCanceledException)
      {
         Interruption.Current.CheckAndThrow();
         return new { error = ToolFailure.Logged(exception, label) };
      }
   }

   JObject Arguments(JToken item)
   {
      var arguments = item is JObject itemObject ? (JObject)itemObject.DeepClone() : new JObject { [_item] = item };
      foreach(var shared in _arguments.Properties().Where(x => !_naming.Contains(x.Name)))
      {
         if(arguments[shared.Name] == null)
            arguments[shared.Name] = shared.Value;
      }

      return arguments;
   }

   static string Label(JToken item) =>
      item is JObject symbol
         ? symbol["symbolName"]?.ToString() ?? $"{symbol["filePath"]}:{symbol["line"]}:{symbol["column"]}"
         : item.ToString();
}

/// <summary>
///   A tool result as the text a batch section shows.
/// </summary>
public static class ToolText
{
   public static string Of(object result)
   {
      if(result is string text)
         return text;

      var json = (JObject)JsonOf.Value(result);
      var error = json["error"]?.ToString();
      if(error == null)
         return JsonOf.Indented(json);

      json.Remove("error");

      return json.Count == 0 ? $"error: {error}" : $"error: {error}\n{JsonOf.Indented(json)}";
   }
}
