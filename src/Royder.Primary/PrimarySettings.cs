using System.Text.Json;
using System.Text.Json.Serialization;
using Royder.Primary.Hosting;

namespace Royder.Primary;

/// <summary>The private Rider installation and the Git repositories whose main and linked worktrees this primary may open.</summary>
sealed class PrimarySettings
{
   public required SharedRiderSettings Rider { get; init; }
   public required string[] Repositories { get; init; }
   public required string CatalogFile { get; init; }
   public int Port { get; init; } = 23751;
   public int? DesktopPort { get; init; }
   public int IdleSeconds { get; init; } = 600;
   public int FirstCallWaitSeconds { get; init; } = 60;
   public IReadOnlySet<string> GitDirectories { get; private set; } = new HashSet<string>();

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
      if(settings.IdleSeconds < 1 || settings.FirstCallWaitSeconds < 1)
         throw new InvalidDataException("Idle and first-call waits must be positive.");
      foreach(var location in new[] { settings.Rider.ProfileDirectory, settings.Rider.RiderHome, settings.Rider.AssetsDirectory, settings.CatalogFile }
                 .Concat(settings.Rider.ToolsFolder is { } toolsFolder ? [toolsFolder] : []))
         if(!Path.IsPathFullyQualified(location)) throw new InvalidDataException($"Expected an absolute installation path: {location}");
      if(OperatingSystem.IsWindows()) SharedRiderHost.CheckProfile(Path.GetFullPath(settings.Rider.ProfileDirectory));
      settings.GitDirectories = settings.Repositories.Select(repo =>
      {
         if(!Path.IsPathFullyQualified(repo)) throw new InvalidDataException($"Expected an absolute main repository path: {repo}");
         var git = Path.GetFullPath(Path.Combine(repo, ".git"));
         if(!Directory.Exists(git)) throw new InvalidDataException($"Expected a main Git checkout at {repo}.");
         return git;
      }).ToHashSet(StringComparer.OrdinalIgnoreCase);
      return settings;
   }
}
