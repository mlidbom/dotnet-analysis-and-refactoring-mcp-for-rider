using System.Text.Json;

namespace Royder.Primary.Worktrees;

/// <summary>
///   The Git repositories whose main checkout and linked worktrees the primary may open: those its settings file names
///   in <c>repositories</c>, and every main checkout directly in a folder it names in <c>repositoryFolders</c>, such as
///   <c>C:\Dev</c>.
/// </summary>
/// <remarks>
///   Read from the settings file whenever asked, so that a repository cloned into such a folder, or one added to the
///   file, is trusted from the next call. Until 2026-11-26 the primary trusted only the repositories the file named when
///   it started: a checkout of a new one, C:\Dev\Mavalonia, was refused all day, and adding it cost a restart that closed
///   every open worktree (Magnus approved the folder, 2026-11-26).
/// </remarks>
sealed class TrustedRepositories(string settingsFile)
{
   /// <summary>
   ///   The <c>.git</c> directory of the main checkout that <paramref name="gitDirectory"/> names, spelled as the
   ///   settings or the disk spell it, when its repository is trusted; nothing when it isn't.
   /// </summary>
   public string? GitDirectoryOf(string gitDirectory)
   {
      var lists = Read();
      return lists.Repositories.Select(it => Path.GetFullPath(Path.Combine(it, ".git"))).FirstOrDefault(it => RegisteredWorktree.SamePath(it, gitDirectory))
          ?? InATrustedFolder(lists.Folders, Path.GetFullPath(gitDirectory));
   }

   /// <summary>What is trusted, as a refusal of a checkout outside it names it.</summary>
   public string Described
   {
      get
      {
         var lists = Read();
         var parts = lists.Repositories.ToList();
         if(lists.Folders.Length > 0) parts.Add("every main Git checkout directly in " + string.Join(" or ", lists.Folders));
         return string.Join(", ", parts);
      }
   }

   static string? InATrustedFolder(string[] folders, string gitDirectory)
   {
      if(!RegisteredWorktree.SamePath(Path.GetFileName(gitDirectory), ".git") || !Directory.Exists(gitDirectory)) return null;
      var checkout = Path.GetDirectoryName(gitDirectory)!;
      var folder = folders.Select(it => Path.TrimEndingDirectorySeparator(Path.GetFullPath(it)))
                          .FirstOrDefault(it => RegisteredWorktree.SamePath(it, Path.GetDirectoryName(checkout)!));
      var name = folder == null ? null : Directory.EnumerateDirectories(folder).Select(Path.GetFileName)
                                                  .FirstOrDefault(it => RegisteredWorktree.SamePath(it, Path.GetFileName(checkout)));
      return name == null ? null : Path.Combine(folder!, name, ".git");
   }

   (string[] Repositories, string[] Folders) Read()
   {
      var lists = JsonSerializer.Deserialize<TrustLists>(File.ReadAllText(settingsFile), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? throw new InvalidDataException("Missing primary settings.");
      return (lists.Repositories ?? [], lists.RepositoryFolders ?? []);
   }

   /// <summary>The two lists, as the settings file holds them.</summary>
   sealed record TrustLists(string[]? Repositories, string[]? RepositoryFolders);
}
