namespace Royder.Primary.Hosting;

/// <summary>One private Rider profile, its packaged launcher and pinned tools, and the two loopback services it owns.</summary>
internal sealed record SharedRiderSettings(string ProfileDirectory, string RiderHome, string AssetsDirectory, string ToolsBuild,
                                          int RemoteDevPort, int BackendMcpPort, int HeapMiB = 768);
