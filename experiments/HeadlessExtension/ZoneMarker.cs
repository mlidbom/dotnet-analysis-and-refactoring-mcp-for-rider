using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.ReSharper.Feature.Services.Daemon;

namespace Royder.HeadlessExtension;

/// <summary>Apply the same <see cref="DaemonZone"/> requirement as Royder's host.</summary>
[ZoneMarker]
public class ZoneMarker : IRequire<DaemonZone>;
