using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.ReSharper.Feature.Services.Daemon;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   The ReSharper zone the host's components need: they're created only in a backend that runs ReSharper's
///   inspections, the <see cref="DaemonZone"/>. It covers every namespace of the host.
/// </summary>
/// <remarks>
///   Rider 2026.3 runs a second backend process beside each solution's, <c>Rider.LightweightBackend.exe</c>, which
///   loads the plugin too but runs no daemon. Without this marker, the host's components were created there as well:
///   its <see cref="McpShellComponent"/> served an MCP server of its own, on a port of its own, with no solution, and
///   served as the primary whenever it got the base port first (2026-11-17).
/// </remarks>
[ZoneMarker]
public class ZoneMarker : IRequire<DaemonZone>;
