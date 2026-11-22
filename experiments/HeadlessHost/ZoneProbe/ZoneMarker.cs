using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.CommandLine.InspectCode.Environment;

namespace Royder.ZoneProbe;

/// <summary>Limits this package's activator to the InspectCode host environment.</summary>
[ZoneMarker]
public sealed class ZoneMarker : IRequire<IInspectCodeEnvironmentZone>;
