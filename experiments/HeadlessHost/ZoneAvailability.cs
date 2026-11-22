using System.Text.Json;
using JetBrains.Application.BuildScript.Application.Zones;
using JetBrains.Application.Environment;
using JetBrains.Application.Environment.LicenseCheck;
using JetBrains.ProjectModel;
using JetBrains.Util;

namespace HeadlessHost;

/// <summary>Reports the platform's decisions for the two requested zones and their prerequisites.</summary>
static class ZoneAvailability
{
   public static void Print(ISolution solution)
   {
      var catalog = solution.GetComponent<JetEnvironmentZones>();
      var running = solution.GetComponent<RunsProducts.ProductConfigurations>().RunningZones;
      var licenses = solution.GetComponent<ZoneLicenseChecker>();
      var preferences = solution.GetComponent<ZoneUserPreferenceChecker>();
      var expiryFilter = solution.GetComponent<ITimebombZoneFilter>();
      var selected = catalog.AllZoneParts.Where(it => Name(it) is "ICodeEditingZone" or "ExternalSourcesZone").ToJetHashSet();
      catalog.ExpandToPredecessors(selected);
      Console.WriteLine("ZONE-STATUS " + JsonSerializer.Serialize(selected.OrderBy(Name).Select(zone => new
      {
         zone = Name(zone),
         running = running.Contains(zone),
         licenseAllows = licenses.IsZoneAllowed(zone),
         disabledByPreference = preferences.IsZoneExplicitlyDisabled(zone),
         expiryAllows = expiryFilter.IsZoneAllowed(zone)
      })));
   }

   static string Name(ZonePart zone) => zone.ZoneInterfaceType.LocalName.ToString();
}
