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

   /// <summary>
   ///   The host's files, each with the subfolder of the plugin folder it goes in. No symbols file: the host is built
   ///   without one, so that a comment doesn't change its identity.
   /// </summary>
   public IEnumerable<(string Subfolder, string File)> HostFiles =>
   [
      ("dotnet", HostAssembly),
      ("lib", FrontendJarFile)
   ];

   /// <summary>The tools assembly and its symbols.</summary>
   public IEnumerable<string> ToolsFiles => [ToolsAssembly, Path.ChangeExtension(ToolsAssembly, ".pdb")];

   /// <summary>What tells this build's host from others, as the host computes it: the SHA-256 of its assembly.</summary>
   public string HostIdentity => IdentityOf(HostAssembly);

   /// <summary>What tells this build's frontend jar from others: <see cref="FrontendJar.IdentityOf"/>.</summary>
   public string FrontendIdentity => FrontendJar.IdentityOf(FrontendJarFile);

   /// <summary>The SHA-256 of a host assembly, in lowercase hexadecimal: <c>ToolsBuildFolder.HostIdentity</c> in the host.</summary>
   public static string IdentityOf(string hostAssembly) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(hostAssembly)));

   /// <summary>
   ///   The jar <c>composedJar</c> builds, whose descriptor Gradle stamps with the version, the compatibility range and
   ///   the change notes. Its name carries the version; the <c>-base</c> and <c>-instrumented</c> jars beside it are steps
   ///   on the way.
   /// </summary>
   string FrontendJarFile =>
      Directory.GetFiles(Path.Combine(_repositoryRoot, "rider-plugin", "build", "libs"), Host + "-*.jar")
               .Where(it => !it.EndsWith("-base.jar", StringComparison.Ordinal) && !it.EndsWith("-instrumented.jar", StringComparison.Ordinal))
               .OrderByDescending(File.GetLastWriteTimeUtc)
               .First();

   string HostAssembly => Path.Combine(_repositoryRoot, "src", Host, "bin", "Release", "net472", Host + ".dll");
   string ToolsAssembly => Path.Combine(_repositoryRoot, "src", Tools, "bin", "Release", "net472", Tools + ".dll");

   /// <remarks>The tools project references the host's, so building it builds both.</remarks>
   void BuildTheBackend() =>
      ExternalCommand.Run("dotnet", ["build", Path.Combine("src", Tools, Tools + ".csproj"), "-c", "Release", "-v", "quiet", "-nologo"], _repositoryRoot);

   /// <remarks>
   ///   No daemon: one would outlive the build holding the caller's output, and a caller that reads that output to its end,
   ///   such as <c>dotnet test | grep</c>, waited until the daemon went (2026-11-15).
   /// </remarks>
   void BuildTheFrontendJar(RiderInstallation rider) =>
      ExternalCommand.Run(Path.Combine(_repositoryRoot, "rider-plugin", "gradlew.bat"),
                          ["composedJar", "--quiet", "--no-daemon"],
                          Path.Combine(_repositoryRoot, "rider-plugin"),
                          new Dictionary<string, string> { ["JAVA_HOME"] = rider.BundledJavaRuntime });
}
