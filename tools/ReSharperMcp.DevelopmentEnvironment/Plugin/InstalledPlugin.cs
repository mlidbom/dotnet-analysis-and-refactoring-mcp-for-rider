using System.Globalization;

namespace ReSharperMcp.DevelopmentEnvironment.Plugin;

/// <summary>
///   The plugin as installed in one Rider's plugins folder: <c>ReSharperMcp\dotnet</c> for the backend,
///   <c>ReSharperMcp\lib</c> for the frontend jar. Rider loads it when it starts.
/// </summary>
/// <remarks>
///   A running Rider has the backend assembly open, so its files can't be overwritten or deleted. They can be moved
///   aside within the drive, though, and the running Rider keeps using the moved copy until it restarts. So installing
///   moves the current files to a folder of replaced builds and copies the new ones in, and a restart takes the new
///   build. The newest <see cref="ReplacedBuildsKept"/> replaced builds are kept, for
///   <see cref="PutBackTheBuildItReplaced"/> to restore.
/// </remarks>
public sealed class InstalledPlugin
{
  /// <summary>
  ///   How many replaced builds are kept. Every run of the specifications installs a build in the sandbox, so without
  ///   a limit they would pile up.
  /// </summary>
  const int ReplacedBuildsKept = 10;

  readonly string _pluginFolder;
  readonly string _replacedBuildsFolder;

  /// <param name="pluginsFolder">The Rider's plugins folder, which holds a folder per plugin.</param>
  /// <param name="replacedBuildsFolder">Where builds that a newer one replaced are kept, one folder each.</param>
  public InstalledPlugin(string pluginsFolder, string replacedBuildsFolder)
  {
    _pluginFolder = Path.Combine(pluginsFolder, "ReSharperMcp");
    _replacedBuildsFolder = replacedBuildsFolder;
  }

  /// <summary>Replaces the installed build, if any, by <paramref name="build"/>.</summary>
  public void Install(PluginBuild build)
  {
    MoveTheInstalledBuildInto(Path.Combine(_replacedBuildsFolder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture)));
    foreach (var (subfolder, file) in build.Files)
    {
      Directory.CreateDirectory(Path.Combine(_pluginFolder, subfolder));
      File.Copy(file, Path.Combine(_pluginFolder, subfolder, Path.GetFileName(file)));
    }
    DeleteAllButTheNewestReplacedBuilds();
  }

  void DeleteAllButTheNewestReplacedBuilds()
  {
    if (!Directory.Exists(_replacedBuildsFolder)) return;
    foreach (var old in Directory.GetDirectories(_replacedBuildsFolder).Order(StringComparer.Ordinal).SkipLast(ReplacedBuildsKept))
      Directory.Delete(old, recursive: true);
  }

  /// <summary>
  ///   Puts back the build the last <see cref="Install"/> replaced, keeping the build it takes out among the replaced
  ///   builds, marked as rolled back.
  /// </summary>
  public void PutBackTheBuildItReplaced()
  {
    var lastReplaced = Directory.GetDirectories(_replacedBuildsFolder).Where(it => !it.EndsWith("_rolled-back", StringComparison.Ordinal)).Max()
                       ?? throw new InvalidOperationException($"No replaced build in {_replacedBuildsFolder} to put back.");
    MoveTheInstalledBuildInto(Path.Combine(_replacedBuildsFolder, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture) + "_rolled-back"));
    MoveEveryFile(from: lastReplaced, to: _pluginFolder);
    Directory.Delete(lastReplaced, recursive: true);
  }

  void MoveTheInstalledBuildInto(string folder)
  {
    if (Directory.Exists(_pluginFolder)) MoveEveryFile(from: _pluginFolder, to: folder);
  }

  /// <remarks>File by file, not the folder at once: a running Rider holds the backend assembly open, which lets the file
  ///   itself be moved but not the folder it's in.</remarks>
  static void MoveEveryFile(string from, string to)
  {
    Directory.CreateDirectory(to);
    foreach (var folder in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
      Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, folder)));
    foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
      File.Move(file, Path.Combine(to, Path.GetRelativePath(from, file)));
  }
}
