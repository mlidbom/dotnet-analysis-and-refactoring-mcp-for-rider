using System.Diagnostics;
using Vanara.PInvoke;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Screen;

/// <summary>
///   Where the <see cref="Riders.SandboxRider"/>'s windows go: with the Rider the plugin is developed in, on its
///   virtual desktop, but on another monitor, small and in the bottom-left corner.
/// </summary>
/// <remarks>
///   Magnus, 2026-11-14: on the desktop of the Rider Claude develops in, on the monitor that Rider isn't on, and
///   small, so that what he keeps on that monitor, such as Git Extensions, stays in reach.<br/>
///   A new Rider window opens on whichever virtual desktop is active, which needn't be that one, hence the move.
/// </remarks>
static class SandboxWindowPlacement
{
  const int Width = 1400;
  const int Height = 900;

  public static void Apply(IEnumerable<TopLevelWindow> sandboxWindows, int sandboxProcessId) =>
    ScreenThread.Run(() => ApplyOnThisThread(sandboxWindows, sandboxProcessId));

  static void ApplyOnThisThread(IEnumerable<TopLevelWindow> sandboxWindows, int sandboxProcessId)
  {
    var developmentRider = TheDevelopmentRiderWindow(sandboxProcessId);
    var monitor = Monitor.All().FirstOrDefault(it => developmentRider is null || !it.IsTheSameAs(developmentRider.Monitor))
                  ?? Monitor.All()[0];
    var workArea = monitor.WorkArea;
    var bounds = new RECT(workArea.left, workArea.bottom - Height, workArea.left + Width, workArea.bottom);

    foreach (var window in sandboxWindows)
    {
      if (developmentRider is not null) window.MoveToTheDesktopOf(developmentRider);
      window.MoveTo(bounds);
    }
  }

  /// <summary>
  ///   The window of the Rider the plugin is developed in: the one with this repository's solution open, unless it's
  ///   closed.
  /// </summary>
  public static TopLevelWindow? TheDevelopmentRiderWindow(int sandboxProcessId) =>
    TopLevelWindow.AllVisible().FirstOrDefault(it => it.ProcessId != sandboxProcessId
                                                     && it.Title.StartsWith(RepositoryRoot.SolutionName, StringComparison.Ordinal)
                                                     && Process.GetProcessById(it.ProcessId).ProcessName == "rider64");
}
