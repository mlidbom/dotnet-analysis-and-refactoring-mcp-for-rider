#nullable enable
using System;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools.Json;

/// <summary>
///   The JSON form of a tool's answer, which a call asks for with <c>format: "json"</c>: one JSON object, a contract
///   for programs that read the answer, such as the .NET Terminal Symbol Linker. The text form is for the agents that
///   read it, and free to change.
/// </summary>
/// <remarks>
///   A server without the JSON form ignores the argument and answers text, so a reader tells an outdated server by the
///   answer's first character. Keep it so: tools ignore arguments they don't know.
/// </remarks>
public static class JsonFormat
{
   /// <summary>Whether the call asks for the JSON form.</summary>
   public static bool IsAskedFor(JObject arguments) =>
      string.Equals(arguments["format"]?.ToString(), "json", StringComparison.OrdinalIgnoreCase);

   /// <summary>The <c>format</c> argument, as a tool's input schema describes it.</summary>
   public static object Argument => new
                                    {
                                       type = "string",
                                       description = "'json': one JSON object, for programs to read. Lines and columns are 1-based. "
                                                   + "A name is the bare C# identifier. A kind is one of: " + JsonNaming.Kinds + ". "
                                                   + "A failure is {\"error\": ...}, and so is a solution still loading or being indexed. "
                                                   + "Default: text."
                                    };
}
