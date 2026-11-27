using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RemoteDevHost;

namespace Royder.Primary.Transport;

/// <summary>A tool manifest captured from the exact tools build installed for cold starts.</summary>
sealed class ToolCatalog
{
   public JArray Tools { get; }
   public JObject Initialize { get; }
   public string HostHash { get; }

   public bool Provides(string toolName) => Tools.OfType<JObject>().Any(it => (string?)it["name"] == toolName);

   public ToolCatalog(string file, string expectedBuild)
   {
      var manifest = JObject.Parse(File.ReadAllText(file));
      if((string?)manifest["build"] != expectedBuild) throw new InvalidDataException("The tool catalog belongs to another build.");
      Tools = manifest["tools"] as JArray ?? throw new InvalidDataException("Missing catalog tools.");
      Initialize = manifest["initialize"] as JObject ?? throw new InvalidDataException("Missing MCP handshake.");
      HostHash = (string?)manifest["host"] ?? throw new InvalidDataException("Missing catalog host identity.");
      if(!Tools.OfType<JObject>().Any(it => (string?)it["name"] == "list_solutions") || Tools.Count < 2)
         throw new InvalidDataException("The cold catalog must come from a Rider serving its solution tools.");
   }

   public static async Task Capture(RiderEndpoint rider, string build, string output, CancellationToken cancel)
   {
      var before = await VerifiedHost();
      var tools = await rider.Catalog(cancel);
      var handshake = (await rider.Send(Rpc.Request("initialize"), cancel))["result"];
      var after = await VerifiedHost();
      if(before != after || handshake == null || tools.Count < 2) throw new InvalidDataException("The source build or catalog changed during capture.");
      var manifest = new JObject { ["build"] = build, ["host"] = before, ["tools"] = tools, ["initialize"] = handshake };
      var target = Path.GetFullPath(output);
      Directory.CreateDirectory(Path.GetDirectoryName(target)!);
      var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
      try
      {
         await File.WriteAllTextAsync(temporary, manifest.ToString(Formatting.Indented), cancel);
         FileReplacement.Replace(temporary, target);
      }
      finally { File.Delete(temporary); }

      async Task<string> VerifiedHost()
      {
         var health = await rider.Health(cancel);
         var rows = (health?["solutions"] as JArray)?.OfType<JObject>().ToArray() ?? [];
         if(rows.Length == 0 || rows.Any(it => (string?)it.SelectToken("tools.build") != build))
            throw new InvalidDataException($"Capture from a Rider serving only solutions on tools build '{build}'.");
         return rows.Select(it => (string?)it.SelectToken("tools.host")).Distinct().Single()
                ?? throw new InvalidDataException("Missing Rider host hash.");
      }
   }
}
