# Updating the tools without restarting Rider

## Why

Magnus works in three or four Riders at once, each with Claude Code sessions running long iterations. A plugin update
that needs a restart means waiting until every session is done, then restarting every Rider. Rider can't load a
plugin's .NET part without a restart, as far as we know (IntelliJ's dynamic plugins cover only its JVM part). So the
plugin does it itself.

## The design

- **The host**, `src/DotNetAnalysisAndRefactoringMcp`, is what Rider loads as the plugin: the ReSharper components, the
  HTTP server, the disk sync, the dispatch onto ReSharper's threads and locks, the interfaces the tools implement, and
  the loader (`Swapping/`). It changes rarely, and a change to it still needs a restart.
- **The tools**, `src/DotNetAnalysisAndRefactoringMcp.Tools`, are everything that answers a call. The host loads a
  build of them into a collectible `AssemblyLoadContext`, and swaps it for a newer one between calls. Every Rider on the
  machine picks a new build up at its next call, with no restart.

## How it works (built 2026-11-14)

Three words, kept apart (Magnus, 2026-11-14): a tools build is **deployed** to a folder that running Riders serve
from at their next call; the plugin is **installed** in a Rider's plugins folder, which takes effect at its restart;
and **publishing** is uploading the plugin to the Marketplace, nothing else.

- **A tools build** is a folder: the tools assembly, its symbols, and `host.sha256`, the SHA-256 of the host assembly
  it was built against. Folders of builds are named by the time they were deployed, so the newest sorts last. A build
  is written as `<name>.incomplete` and renamed when complete.
- **Which build a host serves:** the newest built against itself, from the first of two folders that has one. First
  the folder `DOTNET_ANALYSIS_AND_REFACTORING_MCP_TOOLS` names (the sandbox's), or else
  `%LOCALAPPDATA%\DotNetAnalysisAndRefactoringMcp\tools` (Magnus's everyday Riders'); then the plugin's own `tools`
  folder, which holds the build it was installed with. A Rider still running an older host keeps serving the newest
  build for that host, until it restarts into the new one.
- **Noticing a new build:** at every call, the host compares the folder's write time with the one it last listed it at.
  That is one file-system query per call; a watcher would have been more code for nothing measurable.
- **No gate:** a build counts the calls it serves. A newer build is served from the next call on; the build it replaces
  takes no new calls and unloads as soon as its last one is done. A call that timed out holds its build until it
  notices its lifetime ended.
- **Loaded from bytes,** not the file, so no build is ever locked and old ones can be deleted while Riders run.
- **Checked freed:** after unloading, the host collects garbage at 1, 2, 4 and 8 seconds until the build's load
  context is gone. One that isn't freed is listed under `tools.notFreed` in `/health`, and named in a note on every
  answer until Rider restarts. `When_the_tools_are_swapped_twice` swaps twice and checks that every build swapped out
  in the run was freed, the first one included: the fixture has it run every tool that changes nothing first.
- **The tools are created anew at every call** (`IMcpToolsEntryPoint.CreateTools`), and the host keeps none of them.
- **Deploying:** `sandbox install` and `everyday deploy` deploy the tools build, which takes effect at the next call.
  Only when the host changed do they install the plugin too, which takes effect at the restart.

## Rules for code in the tools assembly

A swapped-out build is freed only if nothing in the default load context keeps one of its objects, types or methods.

- **No object of the tools assembly handed to Newtonsoft.Json's reflection,** such as an anonymous object to
  `JObject.FromObject` or `JsonConvert.SerializeObject`: Newtonsoft.Json is Rider's copy, and caches each type it
  serializes for good. Answers and input schemas may be anonymous objects; the host turns them into JSON with `JsonOf`,
  which walks their properties itself. Tools that need the JSON themselves call `JsonOf` too.
- **Exceptions are logged as text** (`$"...: {exception}"`), never handed to ReSharper's logger: an exception keeps
  loaded every build its stack trace runs through, for as long as it's kept.
- **No subclass of a ReSharper class whose type ReSharper might keep:** such classes go in the host. ReSharper's
  `StartupMeasurer` keeps the activities it measures while it records, which it does at startup, and
  `DaemonProcessBase` measures its runs under the type of the process. So the daemon process that `get_diagnostics`
  runs, a subclass of `DaemonProcessBase`, kept the first build of each Rider process loaded until it moved to the host
  (`DaemonHighlightingCollector`). Found with a heap dump: `dotnet-dump`'s `gcroot` on the old build's objects led
  through `StartupMeasurer` to the process's `System.RuntimeType`.
- **Nothing registered with ReSharper that outlives a call,** such as a subscription on the solution's lifetime: only
  the request's lifetime.
- **No ReSharper components** (`[SolutionComponent]`, `[ShellComponent]`) in the tools assembly: ReSharper builds its
  container when Rider starts. Whatever has to live as long as the solution belongs to the host.

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
  reflection: the constructor with `isCollectible`, `LoadFromStream` and `Unload`.

## Found along the way

- **The host's bytes changed at every commit,** so every deploy looked like a host change and waited for a restart:
  the SDK wrote the git commit into the informational version, and Source Link into the symbols, whose id is stamped
  into the assembly. The host's project turns the SDK's git queries off (`EnableSourceControlManagerQueries`); a
  rebuild after an empty commit gives the same bytes. Found at the first deploy to the everyday Riders.

- **Rider 2026.3 runs a lightweight backend** (`lightweight-backend.*.log`) beside the solution's, and it loads the
  plugin too, with a solution that has no file. Registered, it made "Multiple solutions are open" of every call that
  names no file, whenever it got the primary port first. The host now skips solutions without a file.

## Not done yet

- **A new tool appears only when the MCP client lists the tools again,** such as at a reconnect: the server doesn't
  send `notifications/tools/list_changed`. Changed tools take effect at once.
- **A peer's tools are listed to the primary once, when it registers.** The primary lists its own build's tools, so
  this matters only for a primary with no solution of its own.
- **The usage guide sent at `initialize`** (`McpInstructions`) is in the host, so changing it needs a restart.

## The rename that came with it

The restructuring is the kind of change upstream wouldn't take, so the fork was renamed with it (Magnus, 2026-11-14):

| Where | Name |
|---|---|
| Marketplace display name (at most 30 characters) | `.NET Analysis&Refactoring MCP` |
| GitHub repository | `dotnet-analysis-and-refactoring-mcp-for-rider`: on GitHub nothing else says it's a Rider plugin, and only the Marketplace name has the naming rules (renaming it on GitHub waits for Magnus's word) |
| Root namespace and assemblies | `DotNetAnalysisAndRefactoringMcp`, `DotNetAnalysisAndRefactoringMcp.Tools` |
| Plugin id | `dev.magnus.dotnet-analysis-and-refactoring-mcp` |
| MCP server name in the projects' `.mcp.json` | `dotnet-analysis-and-refactoring`, replacing `resharper-joshua`, and in the rules that name it |
| Default port | 23751; the original's is 23741, the sandbox's stays 23841 |

Magnus decided against asking JetBrains beforehand whether swapping assemblies at run time passes Marketplace review
(2026-11-14): asking risks a no to something we'd do anyway.

Switching Magnus's Riders over is one install of the renamed plugin, with one last restart; from then on, tool updates
need none. His symbol linker depends on the original plugin (`com.j-light.resharper-mcp`, port 23741), so the original
stays installed beside the renamed one until the symbol linker depends on the renamed one.
