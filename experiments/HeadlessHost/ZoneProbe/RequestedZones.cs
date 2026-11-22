using System;
using JetBrains.Application.Environment;
using JetBrains.ReSharper.Feature.Services;
using JetBrains.ReSharper.Feature.Services.ExternalSources;

namespace Royder.ZoneProbe;

/// <summary>Asks the platform to compose the shell with the features selected for this run.</summary>
[ZoneActivator]
public sealed class RequestedZones : IActivateDynamic<ICodeEditingZone>, IActivateDynamic<ExternalSourcesZone>
{
   bool IActivateDynamic<ICodeEditingZone>.ActivatorEnabled() => Request("editing", nameof(ICodeEditingZone));
   bool IActivateDynamic<ExternalSourcesZone>.ActivatorEnabled() => Request("external-sources", nameof(ExternalSourcesZone));

   static bool Request(string feature, string zone)
   {
      var selection = Environment.GetEnvironmentVariable("ROYDER_ZONE_REQUEST");
      var enabled = selection == "both" || selection == feature;
      Console.Error.WriteLine($"Royder zone request: {zone}={enabled}");
      return enabled;
   }
}
