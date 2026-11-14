using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Plugin;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

namespace DotNetAnalysisAndRefactoringMcp.Specifications;

/// <summary>
///   The sandbox Rider running this working tree's build of the plugin, with a fresh copy of the fixture solution
///   (<c>tests/FixtureSolution</c>) open and indexed. Every specification calls the plugin's tools there, through
///   <see cref="McpServer"/>.
/// </summary>
/// <remarks>
///   Set up once for the whole run: it builds the plugin, puts it in the sandbox, and restarts the sandbox, which takes
///   a minute or two. The sandbox is left running afterwards, so its state can be looked at.
/// </remarks>
public sealed class TheSandboxWithTheFixtureSolution
{
  readonly string _solutionFolder;
  readonly SandboxRider _sandbox;
  readonly PluginBuild _build;

  public TheSandboxWithTheFixtureSolution()
  {
    var rider = RiderInstallation.OfThisUser();
    var sandbox = _sandbox = new SandboxRider(rider);
    _build = PluginBuild.FromTheWorkingTree(rider);

    sandbox.Stop();
    sandbox.Install(_build);
    _solutionFolder = Path.Combine(sandbox.SolutionsFolder, "FixtureSolution");
    CopyTheFixtureSolutionTo(_solutionFolder);
    sandbox.Start(PathOf("FixtureSolution.slnx"));
    WaitUntilTheSolutionResolves();
    CallEveryToolThatChangesNothingOnce();
  }

  /// <summary>The plugin's MCP server in the sandbox.</summary>
  public SandboxMcpServer McpServer { get; } = new(SandboxRider.McpPort);

  /// <summary>
  ///   Deploys this run's tools build to the sandbox again, as a new build, which the sandbox swaps in at its next
  ///   call. Returns the new build's name.
  /// </summary>
  public string DeployTheToolsAgain() => _sandbox.Tools.Deploy(_build);

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

  /// <summary>
  ///   Calls each tool that changes nothing, or in a dry run, on the first tools build of the sandbox's new process,
  ///   whatever order the specifications run in. That build is the first in the process to run each of them, and is
  ///   later swapped out, so what ReSharper keeps from the first run of a feature, which would keep the build loaded,
  ///   shows when <c>When_the_tools_are_swapped_twice</c> checks that every swapped-out build was freed. The daemon's
  ///   first run did keep it (2026-11-14), while every later run let its build go.
  /// </summary>
  /// <remarks>What they answer is the other specifications' business: a call that fails doesn't stop the run.</remarks>
  void CallEveryToolThatChangesNothingOnce()
  {
    var circle = PathOf("Shapes/Circle.cs");
    var exporting = PathOf("Shapes.Drawing/Exporting.cs");
    (string Tool, object Arguments)[] calls =
    [
      ("search_symbol", new { query = "Circle" }),
      ("get_symbol_info", new { symbolName = "Circle" }),
      ("go_to_definition", new { symbolName = "Circle" }),
      ("get_symbol_source", new { symbolName = "Circle" }),
      ("find_usages", new { symbolName = "IShape.Area" }),
      ("find_implementations", new { symbolName = "IShape" }),
      ("get_call_hierarchy", new { symbolName = "Circle.Area", direction = "incoming" }),
      ("get_type_hierarchy", new { symbolName = "Circle", direction = "supertypes" }),
      ("flow", new { symbolName = "Exporting.ExportedLength" }),
      ("list_symbols_in_file", new { filePath = circle }),
      ("browse_namespace", new { namespaceName = "Shapes" }),
      ("get_solution_structure", new { }),
      ("get_file_errors", new { filePath = circle }),
      ("get_diagnostics", new { filePath = exporting, minSeverity = "hint" }),
      ("list_quick_fixes", new { filePath = exporting, line = 9, column = 9 }),
      ("apply_suggestions", new { filePath = exporting, all = true, dryRun = true }),
      ("rename_symbol", new { symbolName = "Circle", newName = "Disk", dryRun = true }),
      ("reload_projects", new { filePath = circle })
    ];
    foreach (var (tool, arguments) in calls)
    {
      try { McpServer.CallTool(tool, arguments); }
      catch (InvalidOperationException) { }
    }

    // The usage guide comes from the tools build too.
    McpServer.Initialize();
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
