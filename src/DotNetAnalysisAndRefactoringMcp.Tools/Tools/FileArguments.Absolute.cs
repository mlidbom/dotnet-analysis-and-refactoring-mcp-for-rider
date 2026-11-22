#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

public static partial class FileArguments
{
   /// <summary>
   ///   The named files as absolute paths; relative ones are taken from <paramref name="root"/>.
   /// </summary>
   public static IReadOnlyList<string> Absolute(JObject arguments, SolutionRoot root) =>
      Of(arguments)
        .Select(root.Absolute)
        .Where(x => x != null)
        .Select(x => x!)
        .Distinct()
        .ToList();
}
