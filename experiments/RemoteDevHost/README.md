# RemoteDevHost

Run the installed Windows Rider Remote Development host on a worktree, with Royder as its own primary and private IDE,
ReSharper, plugin and discovery directories. `start` returns after the requested solution is indexed and serving
the specified tools build. The host stays up after the calling shell exits. Requires Windows x64, PowerShell 7.4 or
later, Git, an installed Rider with Royder, and a restored solution. Tested on Rider 2026.3.0.1, RD-263.7307.325.

## Run

From this checkout in PowerShell, set `$solution` to the solution in the worktree you want to host. Use a dedicated
profile **under the current user's TEMP** and choose free loopback ports. The default Rider installation is
`%LOCALAPPDATA%\Programs\Rider`. Supply `-RiderHome` to use a different installation.

```powershell
$tool = (Resolve-Path .\experiments\RemoteDevHost\RemoteDevHost.ps1).Path
$profileDir = Join-Path $env:TEMP 'royder-headless\my-worktree'
$solution = 'C:\Dev\RoyderRemoteRound9-tool\worktree\ReSharperMcp.sln'
$launch = @{
   Profile       = $profileDir
   Solution      = $solution
   RemoteDevPort = 24931
   RoyderPort    = 24941
   HeapMiB       = 768
   ToolsBuild    = '2026-11-22_04-26-01-2911330'
}

& $tool audit -Profile $profileDir -Phase Before
try {
   $hostStatus = & $tool start @launch
   $hostStatus | Format-List
   & $tool status -Profile $profileDir

   $request = @{
      jsonrpc = '2.0'
      id = 1
      method = 'tools/call'
      params = @{
         name = 'find_usages'
         arguments = @{ solutionName = $hostStatus.solution; symbolName = 'PluginBuild.IdentityOf'; kind = 'method' }
      }
   }
   Invoke-RestMethod "http://127.0.0.1:$($hostStatus.royderPort)/" -NoProxy -Method Post -ContentType 'application/json' -Body ($request | ConvertTo-Json -Depth 5)
} finally {
   & $tool stop -Profile $profileDir
   & $tool audit -Profile $profileDir -Phase After | ConvertTo-Json -Depth 5
}
```

For the patcher experiment, add its **extracted plugin folder** and any environment values it reads to the same splat
before `start`. For example:

```powershell
$launch.ExtraPlugin = @($patcherPluginFolder)
$launch.ExtraEnvironment = @{ ROYDER_EXPERIMENT = 'roslyn-patcher' }
```

Each plugin's top-level directory is copied by name beside Royder. The directory must have the layout Rider expects for
an installed plugin. Duplicate destination names fail. Extra environment values reach the JVM and its children. The
harness records its own environment settings in the plan and keeps the extra values out of that file. The optional
variables cannot replace the harness's private directories, ports or tools build.

## Start, status and stop

- **Start** builds a Java argument file from the Windows amd64 launch record in `product-info.json` and sets the
  `remoteDevHost` starter. It invokes the bundled Java executable directly so the child inherits private `APPDATA`
  and `LOCALAPPDATA`. It sets private `user.home`, temp, all four `idea.*.path` properties, ReSharper directories
  and .NET CLI home. It disables IDE and plugin updates, statistics reporting and shared-index downloads and sets
  `JETBRAINS_NO_CHECK_FOR_UPDATES=1`. It trusts the supplied solution directory in this profile.
- The experiment copies Royder from the installed Rider profile and the named build from
  `%LOCALAPPDATA%\DotNetAnalysisAndRefactoringMcp\tools`. It hashes each copy and checks the build's `host.sha256`
  against the copied host DLL. Readiness checks the solution path, indexing state, exact build, private tools directory
  and host hash on `/health`, the primary role and port on `internal/status`, and the Java PID listening on the RD port.
  On Windows, HTTP.sys can report kernel PID 4 as owner of the Royder HTTP listener.
- **Status** reports the Java PID, observed live job PIDs, both ports and the result of a fresh readiness check.
  Results are PowerShell objects. Use `Format-List` or `ConvertTo-Json` to see every field.
- **Stop** asks the JVM to exit using its private config/system lock, allows up to 25 seconds for shutdown, then ends
  remaining members of its Windows job. The supervisor records and verifies job membership, observed PID/creation-time
  identities and listeners. It never kills by process name or port. The OS also terminates the job if the supervisor
  exits unexpectedly.
- The supervisor checks available physical RAM immediately before launching the host (8 GiB minimum) and polls while
  the host runs (200 ms loop delay, stop below 2 GiB). Java is assigned to the job **before** its initial thread resumes.
  The RD port and Royder's ten-port fallback range must be free, disjoint and outside 23751–23760 and 23841–23842.
  Supervisors reserve these ports against other instances of this harness for the life of the job.

The profile's config and system directories survive a stop. Each start uses fresh plugin, tools, log and evidence
directories under `runs/<run-id>`. The returned `evidence` directory contains the Java arguments, indexed health,
private stdout/stderr and `state.json`, including the PID history and shutdown result. The private discovery records
can contain join links.

## Audit

`audit -Phase Before` backs up and hashes real shared roaming files outside versioned JetBrains product folders,
settings tracked by the home Git repository and ordinary settings in the installed Rider profile. It records real
Local JetBrains top-level entry names, types and timestamps, file sizes for files, and home Git status for the roaming
JetBrains tree. Each baseline goes in its own `audits/<id>` directory. `audit -Phase After` compares with the most
recent baseline for that profile and writes `after.json` and `comparison.json`. The comparison reports paths and
changed fields. Backups stay in the private profile.

Other IDEs on the machine can write during an experiment. Use the timestamps, process identities and backup to
attribute a change before restoring it. Audit itself only reads the real folders.

## Why jetbrainsd.exe outlives the host

The bundled **JetBrains OS Integration** plugin (`com.intellij.platform.daemon`) starts a per-user daemon for local
IDE discovery, `jetbrains://` URL handling and communication with Toolbox. The extracted daemon relaunches from
`%LOCALAPPDATA%\JetBrains\Daemon\bundles\current`. Here that is the experiment's private Local AppData. Its lifetime
spans clients. JetBrains documents a five-minute idle period after all IDEs and Toolbox disconnect, which explains
why it is still present just after the JVM exits. [JetBrains daemon lifecycle](https://support.jetbrains.com/hc/en-us/articles/40160535179026-What-is-jetbrainsd) · [Daemon installation and relaunch](https://www.jetbrains.com/help/toolbox-app/jetbrainsd.html)

JetBrains documents disabling the OS Integration plugin and, separately, Toolbox's **Enable jetbrainsd** setting.
I found no documented JVM option for this host to end the daemon synchronously with the JVM. In this build
`DaemonApplicationActivity` reads `jetbrainsd.discovery.enabled` and `jetbrainsd.uri.handling.enabled` to enable
specific integrations. Neither switch promises to suppress daemon startup. The harness keeps the bundled plugin
configuration and cleans up the daemon in its own job. [JetBrains plugin settings](https://support.jetbrains.com/hc/en-us/articles/40160631688850-JetBrains-OS-Integration-plugin-what-it-does-and-whether-it-is-safe-to-disable)

## Keeping vcs.xml clean

In round 8 the root Git mapping survived, but Rider removed the following entry from the tracked
`.idea/.idea.ReSharperMcp/.idea/vcs.xml` when the detached worktree lacked the separate catalog checkout:

```xml
<mapping directory="$PROJECT_DIR$/.claude-shared" vcs="Git" />
```

The writer is IntelliJ's VCS integration in the host JVM. `NewMappings` logged the missing catalog as an invalid
mapping. In the installed `intellij.platform.vcs.impl.jar`, `VcsRootErrorsHandler.fixAndNotifyIfNeeded` enters its
automatic update branch when `vcs.root.auto.add` is true. Even an empty set of new roots can lead to
`ProjectLevelVcsManagerImpl.setAutoDirectoryMappings`, which calls `NewMappings.cleanupMappings`. The IDE persists
the resulting mappings in project settings. The harness sets **`-Dvcs.root.auto.add=false`** in the private JVM.
This also disables automatic registration of newly detected VCS roots in that host. In the smoke worktree the
catalog was absent and the tracked file retained its pre-launch SHA-256 through shutdown, without a Git restore.

## Verified run

On 22 November 2026 the harness opened a fresh detached worktree of `0e4380c` at the example path, using the ports,
768 MiB heap and tools build above. `status` confirmed indexing, and a position-based `find_usages` on
`PluginBuild.IdentityOf` returned **two usages in two files**. A test environment value reached the Java process.
A repeat start/stop check recorded JVM exit code 0, terminated only the remaining private `jetbrainsd.exe`, and
found zero job members, surviving recorded identities or occupied experiment ports. Its audit matched all 197
roaming files, all 29 Local JetBrains top-level entries and Git status. The earlier audit spanned an everyday Rider
restart and recorded concurrent settings and discovery changes. The target worktree stayed clean and plain
`git worktree remove` removed it. Nothing was deployed. [Round 8 measurements](../../dev_docs/headless-royder/remote-dev-server-lifecycle.md#round-8--direct-java-launch-deletion-and-heap-size)

## Checking source-generator readiness

On Windows with Python 3 and the project SDK installed, run
`python -B experiments/RemoteDevHost/TestSourceGeneratorReadiness.py` from the repository root.
It builds this checkout's host and tools, stages private copies, and runs one host at a time. By default it makes
two cold opens of each fixture on loopback 25031 and 25041–25050. Use `--remote-port` and `--port` to choose
another free set. Every open has its own solution copy, profile and indexes.

The generator case uses the real DevelopmentEnvironment project and the round 3 generator/analyzer sources. At
the first `indexing: false`, `sourceGeneratorsReady: true` response, the checks resolve the generated member and read its analyzer
warning, alternating which tool goes first. The two controls cover an analyzer that generates no code and a
project without any analyzer references. The script prints the temporary evidence directory containing timestamped
health samples, tool answers and the harness's shutdown records.

On RD-263.7307.325, `SourceGeneratorMonitor.UpdateState` treats the disabled worker as initialized and
up to date. Royder therefore first checks Rider's loaded, idle project model for applicable analyzer references.
For a solution that has them, it reads both `ISourceGeneratorMonitor.Initialized` and `IsUpToDate` together
with `RoslynFrontendModel.RoslynReady`. Rider's `RoslynReadyPresenter` publishes that last value from its
worker synchronization monitor. This uses the existing backend state on a nonblocking project-model read.
It creates no subscriptions or protocol extensions and keeps no state across tools builds.

The 22 November 2026 run made six fresh opens with private tools build `readiness-b02fbc838382`.
The intervals below run from the first sample with `indexing: false` and generators still pending to the first
usable health sample. Each query was issued once.

| Fixture | Readiness | First open | Second open | Verified at that response |
|---|---|---:|---:|---|
| DevelopmentEnvironment with the round 9 generator and analyzer | `true` | 4.28 s | 4.48 s | One usage of the generated `Twice` method and the expected Roslyn warning |
| Analyzer only | `true` | 2.48 s | 2.45 s | The expected warning at the fixture method |
| No analyzer references | `null` | 0.22 s | 0.21 s | No analyzer warning or unresolved symbol |

For the two generator opens, the backend logged `IsUpToDate` becoming true 11 ms and 111 ms before the first
positive health response. All six JVMs exited cleanly and their Windows jobs and reserved ports were empty.
