using System.Text.Json;
using System.Text.Json.Serialization;
using Royder.Primary.Hosting;
using Royder.Primary.Worktrees;

namespace Royder.Primary;

/// <summary>The private Rider installation and the Git repositories whose main and linked worktrees this primary may open.</summary>
sealed class PrimarySettings
{
   public required SharedRiderSettings Rider { get; init; }
   public required string[] Repositories { get; init; }
   /// <summary>Folders, such as <c>C:\Dev</c>, every main Git checkout directly in which is trusted as the repositories are.</summary>
   public string[] RepositoryFolders { get; init; } = [];
   public required string CatalogFile { get; init; }
   public int Port { get; init; } = 23751;
   public int? DesktopPort { get; init; }
   /// <summary>How long a managed project stays open with no call before it closes, and with the last one the private Rider.</summary>
   /// <remarks>
   ///   Ten minutes until 2026-11-24: an agent in a worktree called every 11 to 17 minutes, so its calls met a closed
   ///   Rider nearly every time. Each paid a cold start of 28 to 56 seconds, and two were answered "still opening".
   ///   The open Rider takes about 3.8 GB for one small solution.
   /// </remarks>
   public int IdleSeconds { get; init; } = 1800;
   /// <summary>How long the primary takes at most to answer a tool call: see <see cref="Transport.AnswerDeadline"/>.</summary>
   public int AnswerWithinSeconds { get; init; } = 55;
   /// <summary>The repositories it may open, read from the settings file at each use: see <see cref="TrustedRepositories"/>.</summary>
   public TrustedRepositories Trusted { get; private set; } = null!;

   public static PrimarySettings Read(string path)
   {
      var settings = JsonSerializer.Deserialize<PrimarySettings>(File.ReadAllText(path),
         new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
         ?? throw new InvalidDataException("Missing primary settings.");
      var ports = Enumerable.Range(settings.Rider.BackendMcpPort, 10).Append(settings.Rider.RemoteDevPort).Append(settings.Port);
      if(settings.DesktopPort is { } desktop) ports = ports.Concat(Enumerable.Range(desktop, 10));
      var all = ports.ToArray();
      if(all.Any(it => it is < 1 or > 65535) || all.Distinct().Count() != all.Length)
         throw new InvalidDataException("Choose separate primary, desktop, remote-development and backend ports, including each backend's ten-port range.");
      if(settings.IdleSeconds < 1 || settings.AnswerWithinSeconds < 1)
         throw new InvalidDataException("The idle time and the time to answer within must be positive.");
      foreach(var location in new[] { settings.Rider.ProfileDirectory, settings.Rider.RiderHome, settings.Rider.AssetsDirectory, settings.CatalogFile }
                 .Concat(settings.Rider.ToolsFolder is { } toolsFolder ? [toolsFolder] : []))
         if(!Path.IsPathFullyQualified(location)) throw new InvalidDataException($"Expected an absolute installation path: {location}");
      if(OperatingSystem.IsWindows()) SharedRiderHost.CheckProfile(Path.GetFullPath(settings.Rider.ProfileDirectory));
      foreach(var repo in settings.Repositories)
      {
         if(!Path.IsPathFullyQualified(repo)) throw new InvalidDataException($"Expected an absolute main repository path: {repo}");
         if(!Directory.Exists(Path.Combine(repo, ".git"))) throw new InvalidDataException($"Expected a main Git checkout at {repo}.");
      }
      foreach(var folder in settings.RepositoryFolders)
         if(!Path.IsPathFullyQualified(folder) || !Directory.Exists(folder)) throw new InvalidDataException($"Expected an absolute folder of repositories: {folder}");
      settings.Trusted = new TrustedRepositories(Path.GetFullPath(path));
      return settings;
   }
}
