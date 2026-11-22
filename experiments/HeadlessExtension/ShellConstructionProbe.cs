using System;
using System.IO;
using System.Linq;
using JetBrains.Application;
using JetBrains.Application.Environment;
using JetBrains.Application.Parts;

namespace Royder.HeadlessExtension;

/// <summary>Record which CLR and which zones actually constructed this <see cref="ShellComponentAttribute"/> component.</summary>
[ShellComponent(Instantiation.ContainerAsyncAnyThreadSafe)]
public sealed class ShellConstructionProbe
{
   public ShellConstructionProbe(RunsProducts.ProductConfigurations configuration)
   {
      var directory = Environment.GetEnvironmentVariable("ROYDER_PROBE_DIRECTORY")!;
      File.WriteAllText(Path.Combine(directory, "shell.txt"),
                        $"ShellConstructionProbe constructed in process {Environment.ProcessId} on CLR {Environment.Version}.");
      File.WriteAllLines(Path.Combine(directory, "zones.txt"),
                         configuration.RunningZones.Select(it => it.ZoneInterfaceType.LocalName.ToString()).Order());
   }
}
