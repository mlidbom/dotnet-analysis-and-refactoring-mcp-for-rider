using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Plugin;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

/// <summary>
///   The Rider Magnus works in, whose resharper-joshua MCP server every Claude session on the machine uses. A build
///   reaches it only after passing in the <see cref="SandboxRider"/>, and takes effect when Magnus restarts it.
/// </summary>
public sealed class EverydayRider
{
  readonly RiderInstallation _rider;

  public EverydayRider(RiderInstallation rider) => _rider = rider;

  /// <summary>The plugin as installed in the everyday Rider's own plugins folder, under its settings folder.</summary>
  public InstalledPlugin Plugin =>
    new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JetBrains", _rider.SettingsFolderName, "plugins"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotNetAnalysisAndRefactoringMcpEverydayRider", "replaced-builds"));
}
