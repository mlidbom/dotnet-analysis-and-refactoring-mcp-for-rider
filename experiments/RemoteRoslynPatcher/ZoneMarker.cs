using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.ReSharper.Feature.Services.Daemon;

namespace Royder.RemoteRoslynPatcher;

/// <summary>Selects the full solution backend where the Roslyn worker runs.</summary>
[ZoneMarker]
public sealed class ZoneMarker : IRequire<DaemonZone>;
