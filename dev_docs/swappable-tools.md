# Updating the tools without restarting Rider

## Why

Magnus works in three or four Riders at once, each with Claude Code sessions running long iterations. A plugin update
that needs a restart means waiting until every session is done, then restarting every Rider. Rider can't load a
plugin's .NET part without a restart, as far as we know (IntelliJ's dynamic plugins cover only its JVM part). So the
plugin is to do it itself.

## The design

- **The host** is what Rider loads as the plugin: the ReSharper components, the HTTP server, the disk sync, the
  contract the tools implement, and the loader. It changes rarely, and a change to it still needs a restart.
- **The tools** are an assembly the host loads from a folder outside Rider, into a collectible `AssemblyLoadContext`.
  When a new build lands in that folder, the host lets the calls in progress finish, unloads the old context, and loads
  the new one. Every Rider on the machine picks the build up, between calls, with no restart.

## What the experiment showed (2026-11-14)

On the branch `experiment/swappable-tools`: `experiment_swap_tool` loads a build of `experiments/SwappableTool` into a
context of its own, calls it, and unloads the previous one. In the sandbox, Rider 2026.3, whose backend runs on
.NET 10:

- **Swapping works.** Seven versions were served in turn without a restart.
- **Types resolve to Rider's copies.** The swapped tool used ReSharper's project model, and was accepted as the plugin's
  `IMcpTool`: ReSharper's assemblies and the plugin's are in the `Default` context, and a context that doesn't load
  them itself falls back to it.
- **Unloading frees the old copy only if nothing in the `Default` context keeps one of its types.** Answers built as
  anonymous objects were kept by Newtonsoft.Json's contract cache, so their context was never freed. Answers built as
  `JObject` were freed every time.
- **`net472` has no `AssemblyLoadContext`,** and the ReSharper SDK ships `net472` only. The host reaches it through
  reflection, which is three calls: the constructor with `isCollectible`, `LoadFromAssemblyPath` and `Unload`.

## Rules for code in the tools assembly

- **No type of the tools assembly in a cache of the `Default` context.** Answers are text or `JObject`, never anonymous
  objects or the assembly's own types handed to Newtonsoft.Json to serialize.
- **Nothing registered with ReSharper that outlives a call,** such as a subscription on the solution's lifetime: only the
  request's lifetime.
- **No ReSharper components** (`[SolutionComponent]`, `[ShellComponent]`) in the tools assembly: ReSharper builds its
  container when Rider starts. Whatever has to live as long as the solution belongs to the host.
- **The host checks, after each swap, that the old context was freed, and says so when it wasn't,** so that a broken
  rule shows at once instead of as memory slowly growing. A specification swaps twice and checks it.

## Still to decide when building it

- How the host notices a new build: a watcher on the tools folder, or a check of a version file at each call.
- The gate that lets calls in progress finish before a swap: a `SemaphoreSlim`-style gate held across awaits, which
  Compze.Threading deliberately doesn't provide.

## The rename that comes with it

The restructuring is the kind of change upstream wouldn't take, so the fork is renamed with it (Magnus, 2026-11-14):

| Where | Name |
|---|---|
| Marketplace display name (at most 30 characters) | `.NET Analysis&Refactoring MCP` |
| GitHub repository | `dotnet-analysis-and-refactoring-mcp` (renaming it on GitHub waits for Magnus's word) |
| Root namespace and assembly | `DotNetAnalysisAndRefactoringMcp` |
| Plugin id | `dev.magnus.dotnet-analysis-and-refactoring-mcp` |
| MCP server name in the projects' `.mcp.json` | `dotnet-analysis-and-refactoring`, replacing `resharper-joshua`, and in the rules that name it |
| Default port | 23751; the original's is 23741, the sandbox's stays 23841 |

Switching Magnus's Riders over is one uninstall of the original plugin and one install of the renamed one, with one
last restart; from then on, tool updates need none.
