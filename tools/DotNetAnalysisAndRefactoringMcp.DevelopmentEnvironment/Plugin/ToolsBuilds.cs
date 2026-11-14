using System.Globalization;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Plugin;

/// <summary>
///   A folder of tools builds, as the host's <c>ToolsBuildFolder</c> reads it: a subfolder per build, named by the
///   time it was published so the newest sorts last, holding the tools assembly, its symbols, and
///   <see cref="HostFileName"/>. A host serves the newest build built against it, from its next call on.
/// </summary>
/// <remarks>
///   The host loads a build from its bytes, so no build is ever locked, and old ones can be deleted while Riders run.
/// </remarks>
public sealed class ToolsBuilds
{
  /// <summary>Holds the identity of the host the build was built against: <c>ToolsBuildFolder.HostFileName</c> in the host.</summary>
  const string HostFileName = "host.sha256";

  /// <summary>A build being written is named so, and the host skips it: <c>ToolsBuildFolder.IncompleteSuffix</c>.</summary>
  const string IncompleteSuffix = ".incomplete";

  /// <summary>How many builds are kept: every run of the specifications publishes a few.</summary>
  const int BuildsKept = 10;

  public ToolsBuilds(string folder) => Folder = folder;

  public string Folder { get; }

  /// <summary>Publishes the tools of <paramref name="build"/> as the newest build, and returns its name.</summary>
  public string Publish(PluginBuild build)
  {
    var name = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss-fffffff", CultureInfo.InvariantCulture);
    var incomplete = Path.Combine(Folder, name + IncompleteSuffix);
    Directory.CreateDirectory(incomplete);
    foreach (var file in build.ToolsFiles) File.Copy(file, Path.Combine(incomplete, Path.GetFileName(file)));
    File.WriteAllText(Path.Combine(incomplete, HostFileName), build.HostIdentity);
    Directory.Move(incomplete, Path.Combine(Folder, name));
    DeleteAllButTheNewest();
    return name;
  }

  /// <summary>Deletes the newest build, so hosts go back to the one before it at their next call, and returns its name.</summary>
  public string RemoveTheNewest()
  {
    var newest = Builds().LastOrDefault() ?? throw new InvalidOperationException($"There is no tools build in {Folder} to remove.");
    Directory.Delete(newest, recursive: true);
    return Path.GetFileName(newest);
  }

  void DeleteAllButTheNewest()
  {
    foreach (var old in Builds().SkipLast(BuildsKept)) Directory.Delete(old, recursive: true);
  }

  IEnumerable<string> Builds() =>
    Directory.Exists(Folder)
      ? Directory.GetDirectories(Folder).Where(it => !it.EndsWith(IncompleteSuffix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)
      : [];
}
