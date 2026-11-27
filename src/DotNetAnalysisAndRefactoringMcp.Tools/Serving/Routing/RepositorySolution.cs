#nullable enable
using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving.Routing;

/// <summary>
///   The solution a repository names as the one for its files, in a <see cref="FileName"/> at the root of its work tree:
///   <c>{"solution": "Compze.AllProjects.slnx"}</c>. A call that names no solution goes to it, when it lists the
///   projects of the call's files (<see cref="SolutionFit"/>).
/// </summary>
/// <remarks>
///   The path is relative to the work tree's root, so that each worktree's copy of the file names its own checkout's
///   solution. A repository with many solutions in one folder, such as Compze's twenty, needs it: by where its files
///   lie, every one of them fits as well.
/// </remarks>
static class RepositorySolution
{
   public const string FileName = ".royder.json";

   /// <summary>
   ///   The absolute path of the solution that the <see cref="FileName"/> at the root of <paramref name="workTree"/> names,
   ///   or none when it has no such file.
   /// </summary>
   /// <exception cref="UnusableFile">The file names no solution that exists.</exception>
   public static string? Of(string workTree)
   {
      var file = Path.Combine(workTree, FileName);
      if(!File.Exists(file)) return null;

      var named = (string?)Parsed(file)["solution"]
               ?? throw new UnusableFile($"{file} names no solution. It holds the path of one from {workTree}, such as {{\"solution\": \"All.slnx\"}}.");
      var solution = Path.GetFullPath(Path.Combine(workTree, named));
      return File.Exists(solution) ? solution : throw new UnusableFile($"{file} names the solution '{named}', and there is no {solution}.");
   }

   static JObject Parsed(string file)
   {
      try
      {
         return JObject.Parse(FilePaths.TextOf(file));
      }
      catch(JsonReaderException notJson)
      {
         throw new UnusableFile($"{file} isn't a JSON object such as {{\"solution\": \"All.slnx\"}}: {notJson.Message}");
      }
   }

   /// <summary>
   ///   A <see cref="FileName"/> that names no solution that exists: the routing refuses the call with its message,
   ///   which says what is wrong with the file, until someone corrects it or the call names its solution.
   /// </summary>
   public sealed class UnusableFile(string problem) : Exception($"{problem} Correct it, or pass 'solutionName'.");
}
