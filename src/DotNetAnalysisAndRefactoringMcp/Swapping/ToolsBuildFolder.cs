using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using DotNetAnalysisAndRefactoringMcp.Tools;

namespace DotNetAnalysisAndRefactoringMcp.Swapping;

/// <summary>
///   A folder of tools builds, one subfolder each, named so that the newest sorts last: the tools assembly with its
///   symbols, and <see cref="HostFileName"/>, which names the host it was built against.
/// </summary>
/// <remarks>
///   A build is written under a name ending in <see cref="IncompleteSuffix"/> and renamed when it's complete, so a
///   half-written build is never loaded. The folder's own write time changes with every build added, renamed or
///   deleted, so it is listed again only then.
/// </remarks>
public sealed class ToolsBuildFolder
{
  /// <summary>The file in a build folder that holds <see cref="HostIdentity"/> of the host the build was built against.</summary>
  public const string HostFileName = "host.sha256";

  public const string IncompleteSuffix = ".incomplete";

  DateTime _listedAt = DateTime.MinValue;
  string _newest;

  public ToolsBuildFolder(string path) => Path = path;

  public string Path { get; }

  /// <summary>The newest complete build that was built against <paramref name="host"/>, or null if there is none.</summary>
  /// <remarks>Not thread safe: <see cref="SwappableTools"/> calls it under its lock.</remarks>
  public string NewestBuildFor(string host)
  {
    if (!Directory.Exists(Path)) return null;

    var written = Directory.GetLastWriteTimeUtc(Path);
    if (written == _listedAt) return _newest;

    _listedAt = written;
    return _newest = Directory.GetDirectories(Path)
                              .Where(build => !build.EndsWith(IncompleteSuffix, StringComparison.Ordinal))
                              .OrderByDescending(System.IO.Path.GetFileName, StringComparer.Ordinal)
                              .FirstOrDefault(build => IsBuiltFor(build, host));
  }

  static bool IsBuiltFor(string build, string host)
  {
    var hostFile = System.IO.Path.Combine(build, HostFileName);
    return File.Exists(System.IO.Path.Combine(build, McpToolsEntryPoint.AssemblyFileName))
           && File.Exists(hostFile)
           && File.ReadAllText(hostFile).Trim() == host;
  }

  /// <summary>
  ///   What tells one build of the host from another: the SHA-256 of its assembly, in lowercase hexadecimal. The build
  ///   is deterministic, so the same source gives the same identity.
  /// </summary>
  public static string HostIdentity(string hostAssemblyFile)
  {
    using var sha256 = SHA256.Create();
    return string.Concat(sha256.ComputeHash(File.ReadAllBytes(hostAssemblyFile)).Select(it => it.ToString("x2")));
  }
}
