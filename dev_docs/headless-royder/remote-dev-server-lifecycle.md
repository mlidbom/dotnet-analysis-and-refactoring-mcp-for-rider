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
