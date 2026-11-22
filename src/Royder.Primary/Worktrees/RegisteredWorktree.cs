using System.Diagnostics;
using DotNetAnalysisAndRefactoringMcp.Serving;

namespace Royder.Primary.Worktrees;

/// <summary>A trusted main Git checkout or one of its linked worktrees, identified across removal and path reuse.</summary>
sealed record RegisteredWorktree(string Root, string GitDirectory, string Generation)
{
   [System.Text.Json.Serialization.JsonIgnore]
   public bool IsMainCheckout => SamePath(GitDirectory, Path.Combine(Root, ".git"));

   public static RegisteredWorktree? Find(string path, IReadOnlySet<string> trustedRepositories)
   {
      var fullPath = Path.GetFullPath(path);
      var root = RequestedPath.WorkTreeOf(Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath));
      if(root == null) return null;
      var marker = Path.Combine(root, ".git");
      try
      {
         if(Directory.Exists(marker)) return MainCheckout(marker, trustedRepositories);
         return File.Exists(marker) ? LinkedWorktree(root, marker, trustedRepositories) : null;
      }
      catch(FileNotFoundException) { return null; }
      catch(DirectoryNotFoundException) { return null; }
   }

   static RegisteredWorktree? MainCheckout(string marker, IReadOnlySet<string> trustedRepositories)
   {
      var trusted = trustedRepositories.FirstOrDefault(it => SamePath(it, marker));
      if(trusted == null) return null;
      var checkout = Path.GetDirectoryName(trusted)!;
      return new(checkout, trusted, WindowsFileIdentity.Read(checkout) + "/" + WindowsFileIdentity.Read(trusted));
   }

   static RegisteredWorktree? LinkedWorktree(string root, string marker, IReadOnlySet<string> trustedRepositories)
   {
      var pointer = File.ReadAllText(marker).Trim();
      if(!pointer.StartsWith("gitdir: ", StringComparison.Ordinal)) return null;
      var admin = Path.GetFullPath(pointer[8..], root);
      var backPointer = Path.Combine(admin, "gitdir");
      var common = Path.GetFullPath(File.ReadAllText(Path.Combine(admin, "commondir")).Trim(), admin);
      var registeredMarker = Path.GetFullPath(File.ReadAllText(backPointer).Trim(), admin);
      if(!trustedRepositories.Contains(common) || !SamePath(Path.GetDirectoryName(admin), Path.Combine(common, "worktrees")) ||
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
                throw new InvalidOperationException($"'{explicitSolution}' is not a solution in worktree '{Root}'.");
      if(candidates.Length == 1) return candidates[0];
      var enclosing = candidates.Where(it => paths.All(path => IsWithin(path, Path.GetDirectoryName(it)!)))
                                .GroupBy(it => Path.GetDirectoryName(it)!.Length).OrderByDescending(it => it.Key).FirstOrDefault();
      if(enclosing?.Count() == 1) return enclosing.First();
      throw new InvalidOperationException($"Choose a solution with its absolute path in 'solutionName'. Worktree '{Root}' contains: {string.Join(", ", candidates)}");
   }

   public async Task Remove(IReadOnlySet<string> trustedRepositories)
   {
      RequireLinkedForRemoval();
      if(Find(Root, trustedRepositories) != this) throw new InvalidOperationException("Git registration changed before removal.");
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

   public static bool IsWithin(string path, string root) =>
      SamePath(path, root) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
