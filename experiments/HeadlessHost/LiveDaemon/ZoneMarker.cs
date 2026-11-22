using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.CommandLine.InspectCode.Environment;

namespace Royder.LiveDaemon;

[ZoneMarker]
public sealed class ZoneMarker : IRequire<IInspectCodeEnvironmentZone>;
