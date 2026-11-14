using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Processes;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Plugin;

/// <summary>
///   The plugin as built from this repository's working tree: the backend assembly that runs inside ReSharper's host
///   process, and the frontend jar whose descriptor makes Rider load it.
/// </summary>
public sealed class PluginBuild
{
  readonly string _repositoryRoot;

  PluginBuild(string repositoryRoot) => _repositoryRoot = repositoryRoot;

  /// <summary>Builds the backend in Release and the frontend jar, and returns the result.</summary>
  public static PluginBuild FromTheWorkingTree(RiderInstallation rider)
  {
    var build = new PluginBuild(RepositoryRoot.Folder);
    build.BuildTheBackend();
    build.BuildTheFrontendJar(rider);
    return build;
  }

  /// <summary>The files a plugin folder holds, each with the subfolder of the plugin folder it goes in.</summary>
  public IEnumerable<(string Subfolder, string File)> Files =>
  [
    ("dotnet", Path.Combine(BackendOutputFolder, "DotNetAnalysisAndRefactoringMcp.dll")),
    ("dotnet", Path.Combine(BackendOutputFolder, "DotNetAnalysisAndRefactoringMcp.pdb")),
    ("lib", Path.Combine(_repositoryRoot, "rider-plugin", "build", "libs", "DotNetAnalysisAndRefactoringMcp.jar"))
  ];

  string BackendOutputFolder => Path.Combine(_repositoryRoot, "src", "DotNetAnalysisAndRefactoringMcp", "bin", "Release", "net472");

  void BuildTheBackend() =>
    ExternalCommand.Run("dotnet", ["build", Path.Combine("src", "DotNetAnalysisAndRefactoringMcp", "DotNetAnalysisAndRefactoringMcp.csproj"), "-c", "Release", "-v", "quiet", "-nologo"], _repositoryRoot);

  void BuildTheFrontendJar(RiderInstallation rider) =>
    ExternalCommand.Run(Path.Combine(_repositoryRoot, "rider-plugin", "gradlew.bat"), ["jar", "--quiet"], Path.Combine(_repositoryRoot, "rider-plugin"),
                        new Dictionary<string, string> { ["JAVA_HOME"] = rider.BundledJavaRuntime });
}
