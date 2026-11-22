# Rider Remote Development host lifecycle on Windows

The stock Windows launcher restores the real AppData paths before starting its JVM. Private IDE directories do give an
instance its own startup lock, but they leave Remote Development discovery and the JVM’s permanent-ID files shared.
A connected controller can close one project in a shared host. A clientless caller has no equivalent stock command.
Both Royder file operations have handlers in the host JVM.

**Scope.** Rider 2026.3.0.1, build `RD-263.7307.325`. The JARs and plugin descriptors are from
`%LOCALAPPDATA%\Programs\Rider`. The .NET IL is from the matching installation under
`C:\Program Files\JetBrains\Rider\r2r`. “Verified” below identifies installed code, JetBrains documentation, or a
specific observation already recorded in round 6. “Inferred” identifies behavior still awaiting a live check.
This study read the binaries and earlier logs without starting a product process. [Round 6 observations](rider-remote-dev-server.md#round-6---observed-on-magnuss-machine-22-november-2026)

## 1. Where discovery and the permanent IDs go

| Writer | Verified path selection in this build |
|---|---|
| Remote Development discovery | `UnattendedHostRecentTrackingHelper.startCommonStatusRenewal` writes a file named by the JVM PID under both `active` and `recent`. It arranges to delete the active file on exit. Its `projectRecentTrackingLoop` also writes a recent entry named by PID and a stable project-path hash. `RemoteDevPaths.localCacheRootDir` calls `PathManager.getDefaultSystemPathFor("RemoteDev")`. On Windows that reads `LOCALAPPDATA` and assembles `<local AppData>/<vendor>/RemoteDev`. The default vendor is `JetBrains`. The ordinary `idea.system.path` override is read by a different getter. |
| JVM permanent IDs | `JetBrainsPermanentInstallationID.syncWithSharedFile` reads `APPDATA` and appends `JetBrains/PermanentUserId`. On Windows, `DeviceIdManager.getOrGenerateId` uses `DeviceIdManager.syncWithSharedFile` for the base recorder’s `JetBrains/PermanentDeviceId`. Both methods can call `Files.writeString` when the existing text already equals the chosen ID. That accounts for how a timestamp can change with identical bytes. Both hard-code the `JetBrains` directory. |
| ReSharper permanent IDs | `JetBrains.Platform.Shell.dll` has its own `PermanentUserId` and `PermanentDeviceId`. Their file store goes through `SharedStorageAccess.GetOrCreateCompanySpecificRoamingFolderFilePersister`. In `JetBrains.Platform.Util.dll`, `CompanySpecificFolderLocations.RoamingAppdata` uses `JET_ROAMING_APPDATA_DIR` directly when supplied. Its Windows fallback is the roaming ApplicationData known folder plus `JetBrains`. This override applies to the .NET file store. |

**Why the parent environment did not contain the writes.** Before creating its launch configuration,
the Windows native launcher's `ensure_env_vars_set` calls `SHGetKnownFolderPath` for `FOLDERID_RoamingAppData`
and `FOLDERID_LocalAppData` and overwrites **both** process environment variables. The installed executable contains
code references to those two folder IDs and both variable names in that startup sequence. Round 6’s driver passed
private values in the environment block to `CreateProcess`. The reset explains the real discovery files, whose
contents identified that JVM. The two timestamp changes are consistent with the JVM writers. Round 6 also saw later
touches from outside its job, so timestamps alone cannot identify the writer. [JetBrains Windows launcher source](https://github.com/JetBrains/intellij-community/blob/aac269f41b10472bda9c45d90d87ea9e3bdb87a7/native/XPlatLauncher/src/lib.rs#L111-L115) · [Known-folder reads and environment assignments](https://github.com/JetBrains/intellij-community/blob/aac269f41b10472bda9c45d90d87ea9e3bdb87a7/native/XPlatLauncher/src/lib.rs#L173-L182)

**What can move.** `PathManager.vendorName()` reads the JVM property `idea.vendor.name`. Rider’s installed
`product-info.json` supplies `-Didea.vendor.name=JetBrains`, and the native launcher appends product arguments
after user VM options. Putting another vendor in `RIDER_VM_OPTIONS` therefore does not select a private directory.
Even a different effective vendor would change just that path component and would leave both literal JVM ID paths
in place. [JetBrains launcher VM option order](https://github.com/JetBrains/intellij-community/blob/aac269f41b10472bda9c45d90d87ea9e3bdb87a7/native/XPlatLauncher/src/default.rs#L51-L65)

I found no RemoteDev-specific directory switch or per-ID directory override in these writers. For strict
same-machine isolation with the stock executable, a separate Windows user profile supplies different known folders.
The installed `bin/rider.bat` starts Java directly and inherits AppData from its caller. Whether it can reproduce the
native remote launcher's complete startup with private roots is a separate, untested route.

## 2. Closing one project

**Verified.** The native command table maps `run` to `remoteDevHost` and `stop` to `exit`.
`ExitStarter.executeCommand` calls `forceExitApplication`. A solution path after `stop` never selects a project to close.
Round 6 observed A and B exit together. I found no project-close entry in the Windows command table or the
`serverMode` starter. [Native command table](https://github.com/JetBrains/intellij-community/blob/aac269f41b10472bda9c45d90d87ea9e3bdb87a7/native/XPlatLauncher/src/remote_dev.rs#L355-L375)

The project-scoped operation is **`CodeWithMeConnectionExtModel.closeProject(RdProjectId)`**, an RD signal.
`DisconnectOnProjectClosingListener.projectClosed` can send it for the closing project from the controller.
`intellij.platform.backend.split.xml` registers `CloseProjectListener` on the `IDE Backend` endpoint. The listener
checks `ClientAppSession.isController`, resolves the ID to the open project and calls
`ProjectManagerEx.saveAndForceCloseProject(project)`. The JVM and the other project are outside that operation.
This requires a controller session. Closing a client window can also just disconnect, leaving the project open.
[JetBrains remote project lifecycle](https://www.jetbrains.com/help/rider/Work_inside_remote_project.html)

## 3. Idle with no client

**Inferred for an ordinary clientless `run`.** Expect the project and JVM to stay open until explicitly closed.
I found no idle-exit or idle-project-close deadline on this path, and no setting on that path to tune one.
Round 6 covers roughly a minute, so it does not establish long-term behavior.

**Verified distinctions.** `UnattendedHostActivityTracker` starts a monotonic clock at construction, reports elapsed
seconds and checks whether a controller exists. `UnattendedStatusUtil` and the Toolbox status handler publish that
information. The status renewal loop writes discovery files. Neither path schedules an IDE exit. There is an actual
`RdServerConnectionManager.closeProjectAbandonedByClient`, reached from a client project-session handshake that
reports abandonment. A project that has never had a client does not enter that handshake. JetBrains documents a
five-minute default for the **Toolbox remote agent** after disconnection. Its `agent_idle_timeout_ms` controls that
agent, a separate process. [Toolbox agent timeout](https://www.jetbrains.com/help/toolbox-app/remote-agent-timeout-machanism.html)

## 4. Two independent JVMs and their ports

**Verified mechanism.** `com.intellij.platform.ide.bootstrap.StartupUtil.lockSystemDirs` passes
`PathManager.getConfigDir()` and `getSystemDir()` to `DirectoryLock`. Its PID lock is `<config>/.lock` and its
activation endpoint is `<system>/.port`. It uses a local-domain socket when supported and otherwise binds
`127.0.0.1:0` and records the assigned port. A second invocation with the same directories can therefore send its
command to the first process. The PID-named `RemoteDev` entries are discovery records, outside this lock.
JetBrains documents distinct IDE directories for multiple standalone Rider processes.
[Running independent Rider processes on Windows](https://support.jetbrains.com/hc/en-us/articles/40140495482002-How-to-launch-the-same-Rider-version-multiple-times-as-standalone-processes)

**Inferred for remote Rider.** Two hosts with different config **and** system directories, different worktrees and
different explicit RD listener ports can run as separate JVMs. I found no product-wide single-instance lock in their
startup path. The round 6 pair shared one directory set and one JVM. The independent pair has not been run here.

| Listener | What determines the port | Evidence from the round 6 pair in one JVM |
|---|---|---|
| Remote Development | `UnattendedStarterUtil` turns an explicit `--port` into `RdServerPort.Fixed`. The default first-available search starts at `REMOTE_DEV_PORT` or 5990. `--ssh-link-port` describes the SSH endpoint for the client link. | 24931 for the JVM |
| IntelliJ built-in HTTP server | `BuiltInServerManagerImpl` starts from JVM property `rpc.port` or 63342. It tries 20 candidate ports and permits an automatically assigned fallback. `UnattendedHostAdditionalBuiltinServerPort` can add a separately configured listener when `CWM_BUILTIN_SERVER_EXPOSED` is enabled. | 63344 |
| Optional plugin listener | The bundled Air plugin’s `AgentCallbackPortServerManager` reuses a saved port or picks from 20000–32767. It stores the choice under `<system>/air/agent-callback-port`. | 26053 in the host log |
| Rider JVM ↔ ReSharper and worker IPC | `ProtocolManager` gives each full backend its own socket port. MSBuild, Roslyn and other enabled workers have their own IPC. | `Rider.BackendHost.exe --Port` was 63489 for A and 55330 for B. A’s logs also identify MSBuild 50525, Roslyn 50554 and DPA 50577. |
| Royder HTTP | Royder’s base port and primary/peer allocation are independent of the Remote Development listener. | Primary 24941 and peer 24942 |

The observed ports above come from the earlier `idea.log` and backend logs. Enabled plugins and workers determine
the full socket set for any particular run.

## 5. Who answers rescan and save

**Verified wiring.** The host JVM plays two roles. It serves JetBrains Client over Remote Development and acts as the
IntelliJ frontend of each local .NET backend. `ProtocolManager.createProtocolHostWithBackend` constructs
`ProtocolHostWithBackend` for that backend. The latter creates a frontend app session and a
`ProtocolExtListenerManager` with endpoint name `IDE Frontend`. `RiderProtocolProjectSessionsManager` creates and
registers a frontend session for the local project using that backend’s solution. None of those steps waits for a
JetBrains Client. Rider’s descriptor requires `intellij.rider.rdclient.dotnet` when `intellij.platform.backend` is
available. The never-connected JVM’s round 6 log records registry contributions from `intellij.rider.rdclient.dotnet.xml`.

| Royder call | Handler in the host JVM |
|---|---|
| `Rescans` sends `FileSystemModel.RefreshPaths` with `RdFsRefreshRequest(paths, false)`. | `intellij.rider.rdclient.dotnet.xml` registers `FileSystemModelProtocolListener` as a solution listener on `IDE Frontend`. It delegates to `VfsWriteOperationsHost.refreshPaths`, which resolves the paths and submits them to IntelliJ’s `RefreshQueue.refreshPaths`. A refresh answer alone does not establish that the project model has seen a new file. |
| `UnsavedDocuments` calls `SaveDocument` for an edit still differing on disk. | For a project file, `Rider.Backend.dll`’s `RiderDocumentOperationsImpl.SaveDocument(document)` calls `SaveOnFrontend`, which starts `RiderProjectDocumentOperationModel.SaveFiles`. In the same JVM module, `DocumentHostHelper.ProtocolListener` binds that call to the frontend project service `DocumentHostHelper`. Its `saveFiles` resolves each virtual file and document and reaches `FileDocumentManager.saveDocumentAsIs`. Royder separately checks disk. |

**Inferred outcome.** The clientless remote host has the same receivers as desktop Rider. Round 6’s rename preview
did not require a persistent edit, and it did not trace a named rescan. The remaining end-to-end checks are an
externally created source file becoming a project item and the exact edited bytes appearing on disk. A successful
edit alone would also be consistent with Rider’s ordinary save winning before Royder invokes its fallback.

## Round 8 — direct Java launch, deletion and heap size

**The clientless host worked at both `-Xmx1024m` and `-Xmx768m`.** Starting the bundled Java executable directly
put the Remote Development status files and both JVM permanent-ID files in the private profile. With tools build
`2026-11-22_04-26-01-2911330`, a deleted declaration disappeared on the first name-only call in all four trials.
The host JVM logged the parent-folder refresh during each first call. The 256 MiB reduction in the heap limit
produced only a 10.7 MiB difference in median private memory for the whole process tree in the two complete runs.

### Launch and profile audit

I added `C:\Dev\RoyderRemoteRound8\worktree` as a detached worktree of `main` at `358ac91` and restored its
`ReSharperMcp.sln`. Each launch used a fresh private profile and IDE caches under
`%TEMP%\royder-headless\codex-3-round-8`. The OS file cache and existing NuGet fallback packages were warm.
I copied the installed Royder plugin and the specified deployed tools directory, verified the copied tools files
against their originals, and matched `host.sha256` to the copied host DLL.

I started `jbr/bin/java.exe` with an argument file built from the Windows amd64 entry in `product-info.json`:
the default VM options with a single replacement `-Xmx`, the expanded `additionalJvmArguments`, the
`bootClassPathJarNames` from Rider’s `lib`, and the declared main class. The application arguments were:

~~~text
com.intellij.idea.Main remoteDevHost C:/Dev/RoyderRemoteRound8/worktree/ReSharperMcp.sln
  --listenOn 127.0.0.1 --port 24931 --ssh-link-host 127.0.0.1 --ssh-link-user <current-user> --ssh-link-port 22
~~~

The JVM received private `APPDATA`, `LOCALAPPDATA`, `user.home`, `java.io.tmpdir` and all four `idea.*.path`
directories. ReSharper’s local, roaming, temp and log directories, process `TEMP` and `TMP`, and `DOTNET_CLI_HOME`
were private too. The profile disabled IDE and plugin updates, statistics reporting and shared-index downloads.
No JetBrains Client connected. Before each launch, 24931 and 24941–24950 were free. The Java PID held 24931.
Royder’s `internal/status` and the private backend log identified it as the primary on 24941. Its `/health` named
the exact worktree solution, the private tools folder, the requested build and host SHA-256
`7529fd2e44f63a15a6f0473b681767eea4fbb2591bbe74328433e4ec1d1d7660`.

**Verified on disk.** Both complete runs started without private IDs or discovery records. While indexed, each had
`%LOCALAPPDATA%\JetBrains\RemoteDev\active\<java-pid>`, a matching `recent\<java-pid>` and a per-project recent
record under its **private** Local AppData. The records identified the worktree and reported
`controllerConnected=false`. The active file disappeared on exit and both recent records remained. Each private
Roaming AppData contained `JetBrains\PermanentUserId` (36 bytes) and `JetBrains\PermanentDeviceId` (43 bytes).

The real-profile audit covered 48 shared roaming files outside versioned IDE folders, 197 files including tracked
files and ordinary Rider settings, and all 29 Local JetBrains top-level entries. The two real IDs kept their bytes and
last-write times. Real `Local\JetBrains\RemoteDev` was absent before and after. All 29 local entries matched on
name, type, size and timestamps. Home Git status for the roaming JetBrains folder was clean at both ends.
The only roaming delta was rotation of a zero-byte consent release log. The baseline filename was stamped
05:40:14, an intermediate one 05:48:36, and the final one 05:50:14. Round 7 had already recorded rotations at
05:10:14, 05:20:14 and 05:30:14. The snapshots cannot attribute the intermediate rotation to a PID. I left the
current log in place. No attributable real-profile file needed restoration.

### Deletion seen by the first caller

I verified that the tracked type `DotNetAnalysisAndRefactoringMcp.Disk.DocumentNotSaved` was present, deleted
`src/DotNetAnalysisAndRefactoringMcp.Tools/Disk/DocumentNotSaved.cs`, and issued the two calls back-to-back.
Only the solution selector and symbol arguments were supplied. I restored the file with Git, checked its bytes and
confirmed that the symbol was visible again before repeating with the opposite tool first. No project reload was
issued. Local times below come from the HTTP request and the private JVM trace for
`VfsWriteOperationsHost`. Each trace names the requested parent `src/DotNetAnalysisAndRefactoringMcp.Tools/Disk`.

| Heap | First call | Request sent | JVM received folder refresh | First reply | Following call |
|---|---|---|---|---|---|
| 1024 MiB | `search_symbol` | 05:50:11.350 | 05:50:11.366 | 0 results in 535 ms | Empty declarations |
| 1024 MiB | `go_to_definition` | 05:50:13.055 | 05:50:13.067 | Empty declarations in 123 ms | 0 results |
| 768 MiB | `search_symbol` | 05:48:43.124 | 05:48:43.650 | 0 results in 799 ms | Empty declarations |
| 768 MiB | `go_to_definition` | 05:48:44.393 | 05:48:44.395 | Empty declarations in 259 ms | 0 results |

All eight replies had `isError=false`. The first requests started 0.3–103 ms after the timestamp immediately before
deletion. The trace establishes receipt in the JVM, and the positive, deleted and restored lookups establish the
project-model result. The IDE file watcher was also enabled, so this run does not establish how quickly the model
would have caught up without Royder’s request.

### Indexed memory, tool answers and garbage collection

At both caps, `find_usages` on `PluginBuild.IdentityOf` returned two usages in two files. A `rename_symbol` preview
named `PluginBuild.cs` and `InstalledPlugin.cs`, with `dryRun=true`, `applied=false`, no conflicts and both
files byte-identical afterward. `complete_at` after `_logger.` in `McpShellComponent.cs` returned 134 items,
including `Warn`. These were calls to the private 24941 primary.

| Complete run | 1024 MiB | 768 MiB |
|---|---:|---:|
| Available physical RAM immediately before JVM start | 16.44 GiB | 14.34 GiB |
| Lowest sampled available RAM | 9.80 GiB | 9.96 GiB |
| Launch to matching indexed health, including 3 s stable | 27.6 s | 28.5 s |
| Indexed sample count and span | 11 over 17.69 s | 10 over 15.96 s |
| Whole job, median private / working set | 4275.6 / 5320.9 MiB | 4264.9 / 5314.6 MiB |
| Logged G1 pauses, total / maximum pause | 71, 377.6 / 21.2 ms | 67, 392.3 / 20.6 ms |
| Full GCs / pauses in indexed sample interval | 0 / 0 | 0 / 0 |

Each cell below gives PID and median **private / working set in MiB** for every process present in the indexed sample
interval. The whole-job figures above are medians of per-sample totals. Summed working sets can count shared pages
more than once.

| Process | 1024 MiB run | 768 MiB run |
|---|---:|---:|
| Host JVM, `java.exe` | 228164 · 1832.1 / 2143.2 | 125848 · 1807.9 / 2117.4 |
| Full `Rider.BackendHost.exe` | 264780 · 1638.2 / 2018.2 | 233572 · 1659.6 / 2040.2 |
| Roslyn `dotnet.exe` worker | 138080 · 544.8 / 708.3 | 213652 · 541.8 / 704.2 |
| MSBuild `dotnet.exe` worker | 12820 · 62.6 / 136.3 | 176632 · 61.3 / 133.7 |
| `JetBrains.DPA.Protocol.Backend.exe` | 98516 · 83.4 / 180.6 | 169896 · 82.0 / 181.5 |
| Private `jetbrainsd.exe` | 265872 · 97.4 / 70.7 | 236124 · 97.0 / 70.4 |
| `fsnotifier.exe` | 228892 · 1.0 / 4.8 | 254504 · 1.0 / 4.8 |
| File watcher’s `conhost.exe` | 269240 · 1.5 / 8.1 | 52208 · 1.5 / 8.1 |
| JVM, backend, Roslyn, MSBuild and DPA `conhost.exe`, respectively | 57156, 138260, 49868, 192936, 177116 · each 1.5 / 8.3 | 234328, 20236, 64136, 244804, 183656 · each 1.5 / 8.3 |

The JVM logs confirmed the two heap maxima. G1 recorded seven pauses associated with humongous allocations at
1024 MiB and six at 768 MiB. Two early pauses at 1024 MiB reported pinned evacuation failures. Neither run logged a
Java heap exhaustion, an `OutOfMemoryError` or a full collection, and neither produced a heap dump. The highest
committed capacities recorded on pause lines were 790 MiB and 768 MiB respectively.
**Inference:** 768 MiB was sufficient for this workload. The runs do not establish a long-lived or larger-solution
heap budget. Across these complete runs the JVM’s median private memory differed by 24.2 MiB. An earlier 1024 MiB
sample on the same worktree gave 4189.7 MiB for the whole job, so the between-run variation here exceeds the
whole-job difference between the two caps.

### Shutdown and repeatability

I launched each JVM suspended, placed it in a private Windows job and then resumed it. A guard enforced the 8 GiB
admission threshold and would terminate only that job below 2 GiB. Two preliminary 1024 MiB runs explain the
sample order. The first indexed successfully but my preflight mistook HTTP.sys’s listener PID 4 for an ownership
failure. I checked the primary status, backend log and build instead. The second passed all three tool checks and
recorded memory, but my positive-control parser expected a hyphen where `search_symbol` returned an em dash.
It stopped before deletion. The complete runs above used fresh profiles at 768 MiB and then 1024 MiB. All four
admissions passed; the lowest sampled physical RAM across all attempts was 8.03 GiB.

The direct Java `exit` command addressed the matching private config/system lock. Both measured JVMs and stop
commands exited with code 0. On each of the four launches a lab-owned `jetbrainsd.exe` remained after JVM exit;
after the 24 s shutdown window the harness terminated the remaining job member. All four jobs then had zero PIDs.
I rechecked recorded PID/creation-time pairs and confirmed 24931 and 24941–24950 were free. After restoring
Rider’s incidental change to the scratch worktree’s tracked `vcs.xml`, plain `git worktree remove` succeeded.
Both the directory and its Git worktree registration are gone.

The private evidence is in `%TEMP%\royder-headless\codex-3-round-8`. The two complete runs are
`heap-768` and `heap-1024-repeat`. Each contains the Java argument file, captured tool calls, JVM refresh and GC
logs, process samples, and `evidence/summary.json`. The root `audit.json` records the final profile, PID and port
checks. The raw discovery records, join links and profile backups remain private.
