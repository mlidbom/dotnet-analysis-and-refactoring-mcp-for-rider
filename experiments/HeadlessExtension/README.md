# Royder as an InspectCode extension

InspectCode 2026.3.0.1 loads Royder's existing `net472` host on .NET 10.0.15.
It opens a solution, serves a Royder symbol query, and shuts down on request.
For an editable worktree, use a host that controls the solution lifetime. InspectCode
holds the solution in the state it expects for a single inspection.

## Run

Run the command from the checkout on Windows. It uses PowerShell 7+, the .NET 10 Windows Desktop
runtime, a .NET 10+ SDK, and the `JetBrains.ReSharper.GlobalTools` 2026.3.0.1 global tool.

```powershell
pwsh -NoProfile -File experiments/HeadlessExtension/Run.ps1
```

The driver builds the host and tools, packs two DLLs, and runs the global tool's own
`inspectcode.exe` on this checkout's `ReSharperMcp.sln`. It selects the installed .NET 10 patch
explicitly. To repeat through the stock `jb inspectcode` launcher, append `-UseJbLauncher`.
The launcher's `net10.0` runtime configuration selects `LatestMajor` and chose .NET 11.0.0 here.
Both modes use `--no-build`, a private solution cache and port 23900. After the query, the driver
signals InspectCode to finish inspecting `ZoneMarker.cs`, checks its exit status, and checks
that the listener has closed.

To compare the read tools with this checkout's open Rider, run
`pwsh -NoProfile -File experiments/HeadlessExtension/Run.ps1 -CompareReadTools` with Python 3 on PATH.
The comparison packs the running Rider's host and copies its tools build into the private tools folder.
It checks both assembly hashes, enables solution-wide analysis, holds the dispatcher after that analysis, and issues matching
requests to ports 23751 and 23900. Each case writes its request and both raw replies under
`comparison/` in the evidence directory. [Recorded comparison](read-tool-results.md)

## Observed in the running process

The [settings experiments](settings-results.md) trace inspection severity to its owning Rider layer, compare read-only mounting strategies, and check both EditorConfig precedence and source line endings.

| Question | Observation |
| --- | --- |
| Can a local extension load? | `-x=Royder.HeadlessExtension --source=<feed>` found `Royder.HeadlessExtension.1.0.0.nupkg` by package ID in a local directory. The DLLs are under `DotFiles/`. The package declares a `Wave` dependency of `[263.0.0]`. InspectCode generated `Royder.HeadlessExtension.JetMetadata.sstg` beside the expanded DLLs and constructed `ShellConstructionProbe`. |
| Which zones run? | `RunsProducts.ProductConfigurations.RunningZones` contained 57 zones. These included `IInspectCodeZone`, `DaemonZone`, `DaemonEngineZone`, `SweaZone`, `IProjectModelZone`, `IHostSolutionZone`, and `ILanguageCSharpZone`. The driver used `--no-swea` to disable solution-wide analysis for the inspection. Each run saves the full zone list. |
| Can an extension keep the solution open? | Yes. InspectCode called `IInspectCodeConsumerFactory.CreateConsumer` before running inspections. The extension pumped `JetDispatcher` there while the driver queried Royder over HTTP. The driver's stop file released the callback. InspectCode wrote the SARIF report and exited with code 0. The probe bounds its wait to 45 seconds. |
| Does the callback run like an editable IDE? | `IFileSystemTracker.Enabled` was false. Acquiring an empty write lock **succeeded**. InspectCode's event handler threw `InvalidOperationException` with “Asking for writer lock unexpected”. ReSharper logged the handler failure and allowed the caller to take the lock. The probe made no source edit. |
| What loads and runs? | `McpShellComponent` listened on 23900, `SwappableTools` loaded a matching private tools build, and `McpServerComponent` registered this checkout's solution. The driver checked the solution path and host SHA-256 in `/health`. `list_symbols_in_file` returned 21 declarations in `McpShellComponent.cs`, including `WatchdogPing`. No assembly-load or component-construction failure occurred for this path. |

## Where the files go

The driver prints a fresh evidence directory under `%TEMP%/royder-headless/<worktree>/`.
All paths in the table are relative to that directory.

| Location | Contents |
| --- | --- |
| `feed/` | The input NuGet package. |
| `local/~/NugetLocalRestore/Royder.HeadlessExtension.1.0.0/` | InspectCode's cached copy of the NuGet package. |
| `local/Shared/vAny/DeployedPackagesExpand/Royder.HeadlessExtension.1.0.0/` | The probe DLL, Royder host DLL and generated JetMetadata. |
| `tools/extension-check/` | The swappable tools DLL and its `host.sha256`. |
| `caches/`, `local/`, `roaming/`, `temp/`, `logs/` | Solution caches, JetBrains application data, temporary data and engine logs. |
| `shell.txt`, `zones.txt`, `waiting.txt`, `health.json`, `symbols.json`, `issues.sarif` | Constructor and runtime evidence, the active zones, callback state, the two HTTP answers, and the inspection report. |

`--source` selects the input feed. `--caches-home` selects the solution cache.
`JET_LOCAL_APPDATA_DIR` redirects **both** extension installation locations above from their
default root, `%LOCALAPPDATA%/JetBrains`. A fresh root also prevents a cached package of the
same version from masking a rebuilt probe. Ordinary .NET build outputs are under the projects'
`bin/` and `obj/` directories. [JetBrains InspectCode documentation](https://www.jetbrains.com/help/resharper/InspectCode.html)

## What the installed binaries explain

- **Command selection.** `CommandLineProductsProgram.Main` in `JetBrains.CommandLine.Products.dll`
  resolves its first argument to a sibling `<command>.exe` and launches it with `dotnet exec`.
  Its help lists `inspectcode` and `cleanupcode`. Extension resolution happens later, inside
  the selected executable. An extension package therefore has no registration point for a new
  top-level `jb` command.
- **Package discovery.** `CommandLinePluginsMixin` resolves the package ID against NuGet sources.
  It supports `id/version-range` and defaults to the JetBrains extension feed when a source is omitted.
  `DeployedPackagesNugetAutoexpand` expands the package and adds its metadata directory to the host.
  `NugetHelpersShell` can synthesize metadata when the manifest declares `Wave`. This experiment
  exercised a local feed with generated metadata.
- **Solution lifetime.** `InspectCodeProductMain.Main` calls `CommandLineProductMain.Main` with
  the default `isLiveSession = false`. This disables `IFileSystemTracker`. Around the inspection it
  handles `ContentModelLocks.BeforeAcquiringWriteLock` by throwing `InvalidOperationException`.
  At the end it disposes the solution lifetime. A longer report callback keeps the dispatcher and
  solution alive within that inspection phase.

## Implication for headless Royder

The extension route supplies JetBrains' executable, package discovery, assembly binding, zones,
and solution startup. It demonstrates that the current host and a read tool can run headless.
A dedicated executable can instead call `CommandLineProductMain.Main(..., isLiveSession: true)`,
own the stop signal and let ReSharper track worktree changes between calls. That is the more direct
fit for a long-lived, editable worktree. The remaining product question is which Royder tools need
changes when Rider's frontend is absent. This experiment exercises one symbol query.

During teardown, `McpShellComponent` logged “MCP server stopped” twice on the same thread.
The process exited normally and released the port. The cause of the duplicate log remains open.
