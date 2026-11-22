using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.CommandLine.InspectCode.Environment;

namespace Royder.Headless.RoslynPatcher;

[ZoneMarker]
public sealed class ZoneMarker : IRequire<IInspectCodeEnvironmentZone>;
