using System.Diagnostics;
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
   /// <summary>
   ///   How many everyday Riders still run the host the installed one replaced: they were started before it was
   ///   installed. They serve the tools built for the installed host only after their restart.
   /// </summary>
   /// <param name="sandbox">The sandbox's process, which runs the same executable and isn't counted.</param>
   public int RunningTheHostBeforeTheInstalledOne(int? sandbox) =>
      Plugin.HostInstalledAt is not {} installedAt
         ? 0
         : Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_rider.Launcher))
                  .Count(it => it.Id != sandbox && it.StartTime.ToUniversalTime() < installedAt);

   public ToolsBuilds Tools => new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DotNetAnalysisAndRefactoringMcp", "tools"));
}
