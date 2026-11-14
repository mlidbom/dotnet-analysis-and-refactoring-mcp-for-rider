using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   The JSON of an object of the tools assembly, such as an anonymous object a tool answers or describes its input
///   with, built by walking its properties: the same JSON Newtonsoft.Json writes for it, without its help.
/// </summary>
/// <remarks>
///   Newtonsoft.Json is Rider's copy, in the default load context, and caches what it learns about each type it
///   serializes for as long as the process runs. A type of a swapped-out tools build in that cache keeps the build
///   loaded: the experiment of 2026-11-14 found that anonymous answers kept every build. Reflection caches what it
///   learns on the type itself, which goes with the build.
/// </remarks>
public static class JsonOf
{
  public static JToken Value(object value) =>
    value switch
    {
      null => JValue.CreateNull(),
      JToken token => token,
      string or bool or char or DateTime or DateTimeOffset or Guid or TimeSpan or Uri or decimal or double or float => new JValue(value),
      // An enum's type code is its underlying integer's: Newtonsoft.Json writes enums as numbers too.
      IConvertible when IsInteger(Type.GetTypeCode(value.GetType())) => new JValue(Convert.ToInt64(value)),
      IDictionary dictionary => new JObject(dictionary.Keys.Cast<object>().Select(key => new JProperty(key.ToString(), Value(dictionary[key])))),
      IEnumerable items => new JArray(items.Cast<object>().Select(Value)),
      _ => new JObject(value.GetType()
                            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                            .Select(property => new JProperty(property.Name, Value(property.GetValue(value)))))
    };

  /// <summary>A tool's answer as the text a caller gets: text as it is, anything else as indented JSON.</summary>
  public static string Text(object answer) => answer as string ?? Value(answer).ToString(Newtonsoft.Json.Formatting.Indented);

  static bool IsInteger(TypeCode code) => code is TypeCode.SByte or TypeCode.Byte or TypeCode.Int16 or TypeCode.UInt16
                                               or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;
}
