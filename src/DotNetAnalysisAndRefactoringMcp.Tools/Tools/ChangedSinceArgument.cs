#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DotNetAnalysisAndRefactoringMcp.Disk;
using JetBrains.ProjectModel;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Tools;

/// <summary>
///   A tool's <c>changedSince</c> argument, a git ref, and its shorthand <c>changed: true</c> for <c>HEAD</c>: every
///   source file changed since then, committed or not, and the untracked ones. <see cref="Expand"/> lists them as the
///   call's <c>filePaths</c> before any lock is taken (<see cref="IMcpExpandableTool"/>).
/// </summary>
static class ChangedSinceArgument
{
   static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cs", ".fs", ".vb" };

   /// <summary>
   ///   <paramref name="arguments"/> with the changed files as <c>filePaths</c>, and the scope they make, such as
   ///   "changed since HEAD", for <see cref="ScopeOf"/>; or why they couldn't be listed, for <see cref="ErrorOf"/>.
   ///   Unchanged when the call asks for no changed files.
   /// </summary>
   public static JObject Expand(JObject arguments, ISolution solution)
   {
      var reference = arguments["changedSince"]?.ToString();
      if(string.IsNullOrEmpty(reference) && arguments["changed"]?.Value<bool>() == true)
         reference = "HEAD";

      if(string.IsNullOrEmpty(reference))
         return arguments;

      var expanded = (JObject)arguments.DeepClone();
      expanded["changedScope"] = $"changed since {reference}";
      try
      {
         var workTree = SolutionRoot.Of(solution).Workspace
                     ?? throw new InvalidOperationException("the solution has no directory");
         expanded["filePaths"] = new JArray(GitChanges.Since(workTree, reference!)
                                                      .Where(it => SourceExtensions.Contains(Path.GetExtension(it)))
                                                      .ToList());
      }
      catch(InvalidOperationException exception)
      {
         expanded["changedError"] = exception.Message;
      }

      return expanded;
   }

   /// <summary>The scope the changed files make, such as "changed since HEAD", if the call asked for them.</summary>
   public static string? ScopeOf(JObject arguments) => arguments["changedScope"]?.ToString();

   /// <summary>Why the changed files couldn't be listed, such as a git ref that doesn't exist.</summary>
   public static string? ErrorOf(JObject arguments) => arguments["changedError"]?.ToString();
}
