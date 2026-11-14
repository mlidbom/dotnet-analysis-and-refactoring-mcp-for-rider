using System.Security.Cryptography;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Processes;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Plugin;

/// <summary>
///   The plugin as built from this repository's working tree. The host, which Rider loads when it starts: its assembly
///   and the frontend jar whose descriptor makes Rider load it. And the tools, which the host loads from a
///   <see cref="ToolsBuilds"/> folder and swaps for a newer build between calls.
/// </summary>
public sealed class PluginBuild
{
  const string Host = "DotNetAnalysisAndRefactoringMcp";
  const string Tools = "DotNetAnalysisAndRefactoringMcp.Tools";

  readonly string _repositoryRoot;

  PluginBuild(string repositoryRoot) => _repositoryRoot = repositoryRoot;

  /// <summary>Builds the host and the tools in Release and the frontend jar, and returns the result.</summary>
  public static PluginBuild FromTheWorkingTree(RiderInstallation rider)
  {
    var build = new PluginBuild(RepositoryRoot.Folder);
    build.BuildTheBackend();
    build.BuildTheFrontendJar(rider);
    return build;
  }

  /// <summary>The host's files, each with the subfolder of the plugin folder it goes in.</summary>
  public IEnumerable<(string Subfolder, string File)> HostFiles =>
  [
    ("dotnet", HostAssembly),
    ("dotnet", Path.ChangeExtension(HostAssembly, ".pdb")),
    ("lib", Path.Combine(_repositoryRoot, "rider-plugin", "build", "libs", Host + ".jar"))
  ];

  /// <summary>The tools assembly and its symbols.</summary>
  public IEnumerable<string> ToolsFiles => [ToolsAssembly, Path.ChangeExtension(ToolsAssembly, ".pdb")];

  /// <summary>What tells this build's host from others, as the host computes it: the SHA-256 of its assembly.</summary>
  public string HostIdentity => IdentityOf(HostAssembly);

  /// <summary>The SHA-256 of a host assembly, in lowercase hexadecimal: <c>ToolsBuildFolder.HostIdentity</c> in the host.</summary>
  public static string IdentityOf(string hostAssembly) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(hostAssembly)));

  string HostAssembly => Path.Combine(_repositoryRoot, "src", Host, "bin", "Release", "net472", Host + ".dll");
  string ToolsAssembly => Path.Combine(_repositoryRoot, "src", Tools, "bin", "Release", "net472", Tools + ".dll");

  /// <remarks>The tools project references the host's, so building it builds both.</remarks>
  void BuildTheBackend() =>
    ExternalCommand.Run("dotnet", ["build", Path.Combine("src", Tools, Tools + ".csproj"), "-c", "Release", "-v", "quiet", "-nologo"], _repositoryRoot);

  void BuildTheFrontendJar(RiderInstallation rider) =>
    ExternalCommand.Run(Path.Combine(_repositoryRoot, "rider-plugin", "gradlew.bat"), ["jar", "--quiet"], Path.Combine(_repositoryRoot, "rider-plugin"),
                        new Dictionary<string, string> { ["JAVA_HOME"] = rider.BundledJavaRuntime });
}
