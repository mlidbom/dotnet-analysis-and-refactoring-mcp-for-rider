using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Plugin;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

/// <summary>
///   The Riders Magnus works in, whose MCP server every Claude session on the machine uses. A build reaches them only
///   after passing in the <see cref="SandboxRider"/>. Its tools take effect at every one of them from its next call on;
///   a change to the host takes effect when Magnus restarts them.
/// </summary>
public sealed class EverydayRider
{
  readonly RiderInstallation _rider;

  public EverydayRider(RiderInstallation rider) => _rider = rider;

  /// <summary>The plugin as installed in the everyday Rider's own plugins folder, under its settings folder.</summary>
  public InstalledPlugin Plugin =>
    new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JetBrains", _rider.SettingsFolderName, "plugins"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotNetAnalysisAndRefactoringMcpEverydayRider", "replaced-builds"));

  /// <summary>
  ///   The tools builds the everyday Riders' hosts serve, the newest built against each: the host's
  ///   <c>SwappableTools.DevelopmentFolder</c>.
  /// </summary>
  public ToolsBuilds Tools => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotNetAnalysisAndRefactoringMcp", "tools"));
}
