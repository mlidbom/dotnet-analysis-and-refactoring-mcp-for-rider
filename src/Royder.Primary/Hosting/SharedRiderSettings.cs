namespace Royder.Primary.Hosting;

/// <summary>One private Rider profile, its packaged launcher and pinned tools, and the two loopback services it owns.</summary>
/// <param name="ToolsFolder">
///   A folder of tools builds the private Rider serves the newest of, as the desktop Riders do, such as the one the
///   development tools deploy to. The pinned build, <paramref name="ToolsBuild"/>, is served while it holds none for the
///   packaged host. Without it, the private Rider serves the pinned build only.
/// </param>
internal sealed record SharedRiderSettings(string ProfileDirectory, string RiderHome, string AssetsDirectory, string ToolsBuild,
                                          int RemoteDevPort, int BackendMcpPort, int HeapMiB = 768, string? ToolsFolder = null);
