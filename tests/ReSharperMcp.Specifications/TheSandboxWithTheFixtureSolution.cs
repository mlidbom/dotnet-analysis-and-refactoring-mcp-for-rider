using ReSharperMcp.DevelopmentEnvironment;
using ReSharperMcp.DevelopmentEnvironment.Plugin;
using ReSharperMcp.DevelopmentEnvironment.Riders;

namespace ReSharperMcp.Specifications;

/// <summary>
///   The sandbox Rider running this working tree's build of the plugin, with a fresh copy of the fixture solution
///   (<c>tests/FixtureSolution</c>) open and indexed. Every specification calls the plugin's tools there, through
///   <see cref="McpServer"/>.
/// </summary>
/// <remarks>
///   Set up once for the whole run: it builds the plugin, installs it, and restarts the sandbox, which takes a minute
///   or two. The sandbox is left running afterwards, so its state can be looked at.
/// </remarks>
public sealed class TheSandboxWithTheFixtureSolution
{
  readonly string _solutionFolder;

  public TheSandboxWithTheFixtureSolution()
  {
    var rider = RiderInstallation.OfThisUser();
    var sandbox = new SandboxRider(rider);
    var build = PluginBuild.FromTheWorkingTree(rider);

    sandbox.Stop();
    sandbox.Plugin.Install(build);
    _solutionFolder = Path.Combine(sandbox.SolutionsFolder, "FixtureSolution");
    CopyTheFixtureSolutionTo(_solutionFolder);
    sandbox.Start(PathOf("FixtureSolution.slnx"));
    WaitUntilTheSolutionResolves();
  }

  /// <summary>The plugin's MCP server in the sandbox.</summary>
  public SandboxMcpServer McpServer { get; } = new(SandboxRider.McpPort);

  /// <summary>The full path of a file of the fixture solution's copy, given its path within the solution.</summary>
  public string PathOf(string pathInTheSolution) => Path.GetFullPath(Path.Combine(_solutionFolder, pathInTheSolution));

  /// <summary>
  ///   Waits until a file that uses the other project and the framework has no errors: only then are the projects
  ///   loaded and restored.
  /// </summary>
  /// <remarks>
  ///   The sandbox reports the solution indexed before Rider's NuGet restore of a fresh copy has finished. The first run
  ///   on 2026-11-14 failed there: <c>System.Object</c> didn't resolve yet.
  /// </remarks>
  void WaitUntilTheSolutionResolves()
  {
    var deadline = DateTime.UtcNow.AddMinutes(2);
    var drawing = PathOf("Shapes.Drawing/Drawing.cs");
    string answer;
    while (!(answer = McpServer.CallTool("get_file_errors", new { filePath = drawing })).Contains("0 diagnostics"))
    {
      if (DateTime.UtcNow > deadline) throw new TimeoutException($"The fixture solution still doesn't resolve after two minutes. get_file_errors answers: {answer}");
      Thread.Sleep(500);
    }
  }

  /// <summary>The copy replaces the last run's, so no run sees what an earlier one changed.</summary>
  static void CopyTheFixtureSolutionTo(string folder)
  {
    if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    var original = Path.Combine(RepositoryRoot.Folder, "tests", "FixtureSolution");
    Directory.CreateDirectory(folder);
    foreach (var subfolder in Directory.GetDirectories(original, "*", SearchOption.AllDirectories).Where(IsSource))
      Directory.CreateDirectory(Path.Combine(folder, Path.GetRelativePath(original, subfolder)));
    foreach (var file in Directory.GetFiles(original, "*", SearchOption.AllDirectories).Where(IsSource))
      File.Copy(file, Path.Combine(folder, Path.GetRelativePath(original, file)));

    bool IsSource(string path) =>
      !Path.GetRelativePath(original, path).Split(Path.DirectorySeparatorChar).Any(it => it is "bin" or "obj" or ".idea");
  }
}
