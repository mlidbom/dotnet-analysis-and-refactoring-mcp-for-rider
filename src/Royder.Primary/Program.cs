using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Royder.Primary;
using Royder.Primary.Hosting;
using Royder.Primary.Transport;
using Royder.Primary.Worktrees;

using var http = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(150) };
if(args is ["capture-catalog", var port, var build, var file])
{
   await ToolCatalog.Capture(new RiderEndpoint(http, int.Parse(port)), build, file, CancellationToken.None);
   return 0;
}
if(args is ["stop", var stopSettings])
{
   await PrimaryControlPipe.Stop(PrimarySettings.Read(stopSettings).Port);
   return 0;
}
if(args is ["remove-worktree", var config, var path])
{
   var options = PrimarySettings.Read(config);
   await PrimaryControlPipe.Remove(options.Port, path);
   Console.WriteLine("Worktree removed.");
   return 0;
}
if(args is ["prepare-catalog", var optionsFile, var solution])
{
   using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
   await PrepareCatalog.Run(PrimarySettings.Read(optionsFile), http, solution, timeout.Token);
   return 0;
}
if(args is not ["serve", var settingsFile])
{
   Console.Error.WriteLine("Royder.Primary serve <settings.json>\nRoyder.Primary stop <settings.json>\nRoyder.Primary prepare-catalog <settings.json> <trusted solution>\nRoyder.Primary capture-catalog <Rider port> <tools build> <catalog.json>\nRoyder.Primary remove-worktree <settings.json> <linked worktree root>");
   return 2;
}

var settings = PrimarySettings.Read(settingsFile);
using var owner = WorktreeJournal.OwnProfile(settings.Rider.ProfileDirectory);
var pinned = new ToolCatalog(settings.CatalogFile, settings.Rider.ToolsBuild);
var host = WebApplication.CreateSlimBuilder();
host.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, settings.Port));
// Without a time, what happened when had to be worked out from the private Rider's own logs (2026-11-24).
host.Logging.AddSimpleConsole(it => it.TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff ");
host.Services.Configure<HostOptions>(it => it.ShutdownTimeout = TimeSpan.FromMinutes(3));
host.Services.AddSingleton(settings);
host.Services.AddSingleton(settings.Rider);
host.Services.AddSingleton(new WorktreeJournal(settings.Rider.ProfileDirectory));
host.Services.AddSingleton(http);
host.Services.AddSingleton(pinned);
host.Services.AddSingleton(TimeProvider.System);
host.Services.AddSingleton(new RiderEndpoint(http, settings.Rider.BackendMcpPort));
host.Services.AddSingleton<OrdinaryRiders>();
host.Services.AddSingleton<RiderReadiness>();
host.Services.AddSingleton<SharedRiderHost>(_ => new SharedRiderHost(settings.Rider, http));
host.Services.AddSingleton<ManagedWorktrees>();
host.Services.AddHostedService(it => it.GetRequiredService<ManagedWorktrees>());
host.Services.AddSingleton<PrimaryToolCatalog>();
host.Services.AddSingleton<PrimaryRequests>();
host.Services.AddHostedService<PrimaryControlPipe>();
host.Services.AddSingleton<ToolCatalogEvents>();
host.Services.AddHostedService(it => it.GetRequiredService<ToolCatalogEvents>());

await using var app = host.Build();
var session = Guid.NewGuid().ToString("N");
app.Use(async (context, next) =>
{
   // Loopback clients may start local processes. Reject browser requests from foreign origins and DNS rebinding.
   var request = context.Request;
   if(request.Host.Host is not ("127.0.0.1" or "localhost") ||
      request.Headers.Origin.Count > 0 && request.Headers.Origin != $"http://{request.Host}")
   {
      context.Response.StatusCode = 403;
      return;
   }
   if(request.Headers.TryGetValue("Mcp-Session-Id", out var clientSession) && clientSession != session)
   {
      context.Response.StatusCode = 404;
      return;
   }
   context.Response.Headers["Mcp-Session-Id"] = session;
   await next(context);
});
app.MapGet("/health", async (PrimaryRequests requests, CancellationToken cancel) => Json(await requests.Health(cancel)));
app.MapGet("/", (HttpResponse response, ToolCatalogEvents events, CancellationToken cancel) => events.Stream(response, cancel));
app.MapDelete("/", () => Results.NoContent());
app.MapPost("/", async (HttpRequest httpRequest, PrimaryRequests requests, CancellationToken cancel) =>
{
   if(!httpRequest.HasJsonContentType()) return Results.StatusCode(415);
   JObject payload;
   try { payload = JObject.Parse(await new StreamReader(httpRequest.Body).ReadToEndAsync(cancel)); }
   catch(JsonReaderException) { return Json(Rpc.Error(null, -32700, "Invalid JSON.")); }
   var answer = await requests.Answer(payload, cancel);
   return answer == null ? Results.Accepted() : Json(answer);
});
await app.RunAsync();
return 0;

static IResult Json(JObject body) => Results.Text(body.ToString(Formatting.None), "application/json");
