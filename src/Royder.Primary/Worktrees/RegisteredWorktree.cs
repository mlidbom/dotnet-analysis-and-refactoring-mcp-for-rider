using System.Diagnostics;
using DotNetAnalysisAndRefactoringMcp.Serving.Routing;

namespace Royder.Primary.Worktrees;

/// <summary>A trusted main Git checkout or one of its linked worktrees, identified across removal and path reuse.</summary>
sealed record RegisteredWorktree(string Root, string GitDirectory, string Generation)
{
   [System.Text.Json.Serialization.JsonIgnore]
   public bool IsMainCheckout => SamePath(GitDirectory, Path.Combine(Root, ".git"));

   public static RegisteredWorktree? Find(string path, TrustedRepositories trusted)
   {
      var fullPath = Path.GetFullPath(path);
      var root = RequestedPath.WorkTreeOf(Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath));
      if(root == null) return null;
      var marker = Path.Combine(root, ".git");
      try
      {
         if(Directory.Exists(marker)) return MainCheckout(marker, trusted);
         return File.Exists(marker) ? LinkedWorktree(root, marker, trusted) : null;
      }
      catch(FileNotFoundException) { return null; }
      catch(DirectoryNotFoundException) { return null; }
   }

   static RegisteredWorktree? MainCheckout(string marker, TrustedRepositories trusted)
   {
      var gitDirectory = trusted.GitDirectoryOf(marker);
      if(gitDirectory == null) return null;
      var checkout = Path.GetDirectoryName(gitDirectory)!;
      return new(checkout, gitDirectory, WindowsFileIdentity.Read(checkout) + "/" + WindowsFileIdentity.Read(gitDirectory));
   }

   static RegisteredWorktree? LinkedWorktree(string root, string marker, TrustedRepositories trusted)
   {
      var pointer = File.ReadAllText(marker).Trim();
      if(!pointer.StartsWith("gitdir: ", StringComparison.Ordinal)) return null;
      var admin = Path.GetFullPath(pointer[8..], root);
      var backPointer = Path.Combine(admin, "gitdir");
      var common = Path.GetFullPath(File.ReadAllText(Path.Combine(admin, "commondir")).Trim(), admin);
      var registeredMarker = Path.GetFullPath(File.ReadAllText(backPointer).Trim(), admin);
      if(trusted.GitDirectoryOf(common) == null || !SamePath(Path.GetDirectoryName(admin), Path.Combine(common, "worktrees")) ||
         !SamePath(registeredMarker, marker)) return null;
      return new(Path.GetDirectoryName(registeredMarker)!, admin, WindowsFileIdentity.Read(marker) + "/" + WindowsFileIdentity.Read(backPointer));
   }

   public static bool SamePath(string? first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

   public async Task<string> SolutionFor(IReadOnlyList<string> paths, string? explicitSolution, CancellationToken cancellation)
   {
      var start = new ProcessStartInfo("git") { WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
                                               RedirectStandardOutput = true, RedirectStandardError = true };
      foreach(var arg in new[] { "--no-optional-locks", "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", "*.sln", "*.slnx" })
         start.ArgumentList.Add(arg);
      using var git = Process.Start(start) ?? throw new IOException("Could not start Git.");
      var output = git.StandardOutput.ReadToEndAsync(cancellation);
      var error = git.StandardError.ReadToEndAsync(cancellation);
      await git.WaitForExitAsync(cancellation);
      if(git.ExitCode != 0) throw new IOException($"Could not list solutions in {Root}: {await error}");
      var candidates = (await output).Split('\0', StringSplitOptions.RemoveEmptyEntries)
         .Select(it => Path.GetFullPath(it, Root)).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
      if(explicitSolution != null)
         return candidates.FirstOrDefault(it => SamePath(it, explicitSolution)) ??
                throw new InvalidOperationException(
                   $"{(File.Exists(explicitSolution) ? $"'{explicitSolution}' is a solution Git doesn't list, as ignored" : $"'{explicitSolution}' doesn't exist")}. "
                 + $"Worktree '{Root}' contains: {(candidates.Length == 0 ? "no solution" : string.Join(", ", candidates))}");
      if(candidates.Length == 0) throw new InvalidOperationException($"Worktree '{Root}' contains no solution.");
      if(candidates.Length == 1) return candidates[0];
      var toRoute = new PathsToRoute(paths);
      var ranked = candidates.Select(it => (Solution: it, Fit: toRoute.FitOf(it, Root))).OrderByDescending(it => it.Fit).ToList();
      if(ranked[0].Fit.CompareTo(ranked[1].Fit) > 0) return ranked[0].Solution;
      throw new InvalidOperationException($"Neither where the files lie nor their projects tell which solution to open for them: pass one's absolute path as 'solutionName', "
                                        + $"or name one in a {RepositorySolution.FileName} at the work tree's root. Worktree '{Root}' contains: {string.Join(", ", candidates)}");
   }

   public async Task Remove(TrustedRepositories trusted)
   {
      RequireLinkedForRemoval();
      if(Find(Root, trusted) != this) throw new InvalidOperationException("Git registration changed before removal.");
      var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = Path.GetDirectoryName(Root)!,
         UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
      foreach(var arg in new[] { "--git-dir", GitDirectory, "worktree", "remove", Root }) start.ArgumentList.Add(arg);
      using var git = System.Diagnostics.Process.Start(start) ?? throw new IOException("Could not run Git.");
      var errors = git.StandardError.ReadToEndAsync();
      await git.WaitForExitAsync(CancellationToken.None); // Hold the lifecycle gate until the dispatched removal finishes.
      if(git.ExitCode != 0) throw new InvalidOperationException((await errors).Trim());
   }

   public void RequireLinkedForRemoval()
   {
      if(IsMainCheckout) throw new InvalidOperationException($"remove-worktree only removes linked Git worktrees. '{Root}' is a main checkout.");
   }
}
