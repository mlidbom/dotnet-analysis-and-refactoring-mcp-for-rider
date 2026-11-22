using System.Diagnostics;
using System.Runtime.InteropServices;
using DotNetAnalysisAndRefactoringMcp.Serving;
using Microsoft.Win32.SafeHandles;

namespace Royder.Primary.Worktrees;

/// <summary>A linked Git worktree, checked against its reciprocal Git registration and its trusted main repository.</summary>
sealed record RegisteredWorktree(string Root, string GitDirectory, string Generation)
{
   public static RegisteredWorktree? Find(string path, IReadOnlySet<string> trustedRepositories)
   {
      var root = RequestedPath.WorkTreeOf(Directory.Exists(path) ? path : Path.GetDirectoryName(path));
      if(root == null) return null;
      var marker = Path.Combine(root, ".git");
      if(!File.Exists(marker)) return null;
      try
      {
         var pointer = File.ReadAllText(marker).Trim();
         if(!pointer.StartsWith("gitdir: ", StringComparison.Ordinal)) return null;
         var admin = Path.GetFullPath(pointer[8..], root);
         var backPointer = Path.Combine(admin, "gitdir");
         var common = Path.GetFullPath(File.ReadAllText(Path.Combine(admin, "commondir")).Trim(), admin);
         var registeredMarker = Path.GetFullPath(File.ReadAllText(backPointer).Trim(), admin);
         if(!trustedRepositories.Contains(common) || !SamePath(Path.GetDirectoryName(admin), Path.Combine(common, "worktrees")) ||
            !SamePath(registeredMarker, marker)) return null;
         return new(Path.GetDirectoryName(registeredMarker)!, admin, FileIdentity(marker) + "/" + FileIdentity(backPointer));
      }
      catch(FileNotFoundException) { return null; }
      catch(DirectoryNotFoundException) { return null; }
   }

   public static bool SamePath(string? first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

   // Git may reuse both names on immediate recreation. NTFS file IDs identify the original pair across that race.
   static string FileIdentity(string path)
   {
      using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
      if(!GetFileInformationByHandle(handle, out var info)) throw new System.ComponentModel.Win32Exception();
      return $"{info.Volume:x8}/{info.IndexHigh:x8}{info.IndexLow:x8}";
   }

   [StructLayout(LayoutKind.Sequential)]
   struct FileInformation
   {
      public uint Attributes;
      public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
      public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
   }

   [DllImport("kernel32.dll", SetLastError = true)]
   static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

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
      if(Find(Root, trustedRepositories) != this) throw new InvalidOperationException("Git registration changed before removal.");
      var start = new System.Diagnostics.ProcessStartInfo("git") { WorkingDirectory = Path.GetDirectoryName(Root)!,
         UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
      foreach(var arg in new[] { "--git-dir", GitDirectory, "worktree", "remove", Root }) start.ArgumentList.Add(arg);
      using var git = System.Diagnostics.Process.Start(start) ?? throw new IOException("Could not run Git.");
      var errors = git.StandardError.ReadToEndAsync();
      await git.WaitForExitAsync(CancellationToken.None); // Hold the lifecycle gate until the dispatched removal finishes.
      if(git.ExitCode != 0) throw new InvalidOperationException((await errors).Trim());
   }

   public static bool IsWithin(string path, string root) =>
      SamePath(path, root) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
