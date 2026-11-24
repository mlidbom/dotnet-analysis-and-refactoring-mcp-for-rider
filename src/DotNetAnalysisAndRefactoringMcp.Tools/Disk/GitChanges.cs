#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   Files git reports as changed in a working tree: modified since a ref (committed or not) plus untracked ones.
/// </summary>
public static class GitChanges
{
   static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

   /// <param name="workTree">The work tree's root folder: the paths git gives are relative to it.</param>
   /// <returns>Absolute paths of changed files that still exist.</returns>
   /// <exception cref="InvalidOperationException">
   ///   Git is missing, <paramref name="workTree"/> is not a work tree, the ref is unknown, or git took longer than
   ///   <see cref="Timeout"/>.
   /// </exception>
   /// <remarks>
   ///   Until 2026-11-24 a <c>git rev-parse --show-toplevel</c> found the root first, which the caller already knows: on
   ///   a machine under heavy load, it was that call that timed out.
   /// </remarks>
   public static IReadOnlyList<string> Since(string workTree, string reference)
   {
      var changed = Git(workTree, "diff", "--name-only", "--diff-filter=d", reference, "--");
      var untracked = Git(workTree, "ls-files", "--others", "--exclude-standard");

      return changed.Concat(untracked)
                    .Select(x => Path.GetFullPath(Path.Combine(workTree, x)))
                    .Where(File.Exists)
                    .Distinct()
                    .ToList();
   }

   static IReadOnlyList<string> Git(string directory, params string[] arguments)
   {
      var start = new ProcessStartInfo("git", string.Join(" ", arguments.Select(Quoted)))
                  {
                     WorkingDirectory = directory,
                     RedirectStandardOutput = true,
                     RedirectStandardError = true,
                     UseShellExecute = false,
                     CreateNoWindow = true,
                  };

      try
      {
         using var git = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
         var output = git.StandardOutput.ReadToEndAsync();
         var error = git.StandardError.ReadToEndAsync();
         if(!git.WaitForExit((int)Timeout.TotalMilliseconds))
         {
            git.Kill();
            throw new InvalidOperationException($"git {arguments[0]} took longer than {Timeout.TotalSeconds} s, as it does on a machine under heavy load: " +
                                                "retry, or name the folders to cover in filePaths");
         }

         if(git.ExitCode != 0)
            throw new InvalidOperationException($"git {arguments[0]} failed: {error.Result.Trim()}");

         return output.Result.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
      }
      catch(System.ComponentModel.Win32Exception exception)
      {
         throw new InvalidOperationException($"git is not available: {exception.Message}", exception);
      }
   }

   static string Quoted(string argument) =>
      argument.Any(char.IsWhiteSpace) || argument.Contains('"')
         ? $"\"{argument.Replace("\"", "\\\"")}\""
         : argument;
}
