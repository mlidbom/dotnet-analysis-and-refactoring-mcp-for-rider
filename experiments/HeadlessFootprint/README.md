<!-- By the Codex helper royder-codex-1, 2026-11-22: measured with the scripts beside this file, in a scratch copy of them in its Temp folder. Kept as it was written, except that its links to the evidence are plain paths: the evidence, 643 MB under cases/, was not kept, and a new run writes the same paths. What later runs disprove is noted in dev_docs/backlog.md. -->

# What an open HeadlessHost does to a Git worktree on Windows

**Plain `git worktree remove` failed after the solution had loaded.** Git removed every file and forgot the worktree, then returned exit 255 and left an empty directory. The process preventing deletion was JetBrains’ **Roslyn worker**. It uses the solution directory as its current working directory. In a separate control, ending that worker alone let Windows grant DELETE access to the directory while the engine and MSBuild continued running.

**`DisablePackageAssetsCache=true` removed exactly three worktree writes from the persistent file set.** Both runs loaded the same three projects and returned the same three `IShape.Area` usage locations. Eighteen other files still appeared in `obj`. A fully loaded host in this fixture used **two simultaneous loopback TCP listeners** for its workers, one each for MSBuild and Roslyn. Both closed on orderly exit and on engine-only termination.

Measured on 22 November 2026 with the stock `experiments/HeadlessHost` from Royder revision `313d5c29d00ed139c15232b5715e70521ccb86e2`, `jb` 2026.3.0.1, .NET 10.0.15 for the engine, and Visual Studio 18 MSBuild. Each case used a fresh, restored, detached worktree of a private one-commit copy of the three-project Shapes fixture (`3a5b91527d3a3a3e1e23230c02edd09155c0f3cd`). I built and ran the program in scratch, with its current directory outside the worktree. I made no changes to the engine or fixture sources. No Royder plugin or sandbox Rider was involved.

## 1. First-attempt removal at four moments

A separate, prestarted PowerShell validated the private root and issued `git -C <private-repository> worktree remove <absolute-worktree-path>` once, without force. Each worktree was clean before loading. Git’s trace and the engine’s stdout were timestamped independently.

| When Git ran | First attempt | State left behind |
|---|---|---|
| **Startup**, before the engine could open the solution | Exit **0** in both `startup` and `startupwatch`. In the first run the root was gone 0.77 s after launch. The engine then reported the missing solution and exited 1. | Root and Git registration gone. In the repeat, the parent-directory watcher recorded **zero** events under that path after Git returned, through 16.8 s of observation including engine exit. The directory remained absent at the final check. |
| **Indexing**, after the MSBuild reload completed and Roslyn initialized, before `INDEXED` | Exit **255**, twice. The stronger repeat (`indexcache`) also waited for a symbol-cache file to appear. Git started at 01:38:11.064 UTC, failed at 01:38:12.185, and the engine reported `INDEXED` at 01:38:12.646. | Registration gone, directory empty and still present after engine exit. Windows denied DELETE access with sharing violation 32. Engine 222676 and Roslyn 237712 held handles to the root. The timing establishes the cache-filling interval; the CLR stack sample alone does not identify an individual source file being indexed at the instant of deletion. |
| **Idle**, after `INDEXED`, the usage result and `READY` | Exit **255** (`idle`). Git reported `Permission denied` on the root. | Registration gone, empty root persisted. Engine 232976 and Roslyn 114516 held root handles. MSBuild held none to the root at measurement. |
| **During the first usage query** (`query`) | Exit **255**. The program announced the three projects immediately before entering `FindUsages`. Git started at 01:34:44.772 UTC. The engine returned the three lines and reported 332 ms of search time; its result was captured at 01:34:45.113. Git exited at 01:34:45.154. | Registration gone, empty root persisted. Engine 219448 and Roslyn 215400 held the root. The parent observer received the first removals while the program was emitting its answer. |

**The handle and its owner.** Windows reports filesystem directory handles as object type `File`. In the idle case the engine had a root directory handle (`0x120089` granted access) and handles to the three project folders before removal. Roslyn had a root directory handle (`0x100020`, including `FILE_TRAVERSE` and `SYNCHRONIZE`) and its current directory was the root. The engine’s current directory was private scratch; MSBuild’s was scratch/temp in the idle capture. A granted access mask alone cannot identify which process denied delete sharing.

In the causal control (`causal`), I repeated the idle removal and measured DELETE access on the empty root. It returned Windows error 32. I terminated only the recorded Roslyn process, PID 236356, checking PID, creation time, worker command line and working directory. **223 ms later DELETE access was granted.** Engine 28580 still held root handle 7336, and both that engine and MSBuild 44788 were alive. The installed `JetBrains.Roslyn.Host.dll` corroborates the observation: `RoslynExternalProcess.GetProcessStartInfo` explicitly sets `WorkingDirectory = solutionPath.Directory.FileAccessPath`. Redirecting just the parent engine’s working directory does not address the child’s assignment.

**Design consequence.** A notification that a worktree has disappeared cannot release a handle that prevented the last directory from disappearing. The partial removal also shows why a remaining directory alone is insufficient evidence that the Git worktree still exists. A design intended to support plain Git removal must address the Roslyn worker’s working directory and retest it with the worker active. The solution sat at the worktree root in this fixture.

Evidence: startup repeat (`cases/startupwatch/removal.json`), indexing repeat (`cases/indexcache/removal.json`), idle handles (`cases/idle/directory-handles-before.json`), query Git trace (`cases/query/git-events.jsonl`), causal control (`cases/causal/release-roslyn.json`), remaining handles after releasing Roslyn (`cases/causal/handles-after-release.json`).

## 2. Writes during load, query and exit

I compared file paths, sizes and last-write times before launch and after exit. I also recorded recursive `ReadDirectoryChangesW` notifications during the run to catch transient files. There was no installed non-elevated PID-attributed filesystem tracer available among the tools checked. The private directories were exclusive to this experiment. Events in shared directories do not identify the writer.

| Root | Full control (`control`) | What was written |
|---|---:|---|
| Fixture worktree | **21 added**, 0 existing files changed or removed | Exactly seven files per project, listed below. The observer also saw nine temporary filenames ending in `~` under the three intermediate directories and creation of `bin/Debug/net10.0`, `ref` and `refint` directories. No assembly under `bin` was left by the run. |
| Redirected `solution-caches` | 29 files | ReSharper’s solution database and per-project, symbol and assembly-resolution caches. |
| Redirected `local` | 26 files | HeadlessHost shell caches and usage statistics. |
| Redirected `roaming` | 4 files | `HeadlessHost/v263/CultureOptions`, permanent user/device IDs and feature-usage salt. |
| Redirected `logs` | 2 files | MSBuild task log and Roslyn worker log. |
| Redirected `temp` | 120 files | Primarily shadow copies of analyzers, source generators and dependencies, plus JetBrains metadata. Monitor notifications also covered temporary project/worker data. **181 files** remained across all five private engine-state roots. |
| Scratch .NET CLI/NuGet environment | 0 file differences | The fixture restore and harness build were complete before the initial snapshots. |
| Everyday JetBrains profiles | No write attributable to the engine | In the control engine’s lifetime the only notified path in `%LOCALAPPDATA%/JetBrains` was an existing everyday Rider backend log. There were zero notifications in `%APPDATA%/JetBrains` during that lifetime. The wider, non-atomic local snapshots saw 26 additions, 26 modifications and 12 removals while other IDEs were active. Those are shared-machine changes. |

**Exact persistent worktree outputs.** For each project `P` in `Shapes`, `Shapes.Drawing` and `Shapes.Web`, the control added:

- `P/obj/project.packagespec.json`
- `P/obj/Debug/net10.0/.NETCoreApp,Version=v10.0.AssemblyAttributes.cs`
- `P/obj/Debug/net10.0/P.AssemblyInfo.cs`
- `P/obj/Debug/net10.0/P.AssemblyInfoInputs.cache`
- `P/obj/Debug/net10.0/P.GeneratedMSBuildEditorConfig.editorconfig`
- `P/obj/Debug/net10.0/P.GlobalUsings.g.cs`
- `P/obj/Debug/net10.0/P.assets.cache`

**Existing settings.** A separate `settings` run started with two writable synthetic files: `.idea/.idea.DotNetAnalysisAndRefactoringMcp.FixtureSolution/.idea/workspace.xml` and `DotNetAnalysisAndRefactoringMcp.FixtureSolution.slnx.DotSettings.user`. Both retained their original SHA-256 **and** modification time through normal exit. That run added the same 21 worktree files. The observed global NuGet local/roaming roots had zero snapshot changes. It left 182 files in private engine state. Shared JetBrains profile snapshots showed background IDE activity, with no fixture-specific write identified.

These observations cover the listed roots on this restored fixture. A metadata comparison can miss same-size writes with restored timestamps and transient writes. Directory notifications can coalesce and have no PID. This is insufficient to establish that arbitrary MSBuild projects cannot write elsewhere. The launcher reproduced `Run.ps1`’s engine-state redirections and additionally kept .NET CLI/NuGet state under scratch.

Evidence: control file-by-file delta (`cases/control/metadata-diff.json`), control notifications (`cases/control/filesystem-events.jsonl`), settings before (`cases/settings/settings-before.json`), settings after (`cases/settings/settings-after.json`), settings-run metadata (`cases/settings/metadata-diff.json`).

## 3. DisablePackageAssetsCache and model parity

**Yes for this fixture and this query.** Stock `HeadlessHost` does not forward arbitrary `--properties` arguments. I set `DisablePackageAssetsCache=true` in the engine child environment, which supplies the MSBuild environment property without changing the program. The control omitted it. Each run used a separately restored worktree of the same commit and a fresh private engine cache.

| Outcome | Control | Property set |
|---|---:|---:|
| Projects reported | 3 | 3 |
| Persistent `*.assets.cache` files added | 3 | **0** |
| Other persistent worktree files added | 18 | **18**, identical relative paths |
| `IShape.Area` usage locations | 3 | **Same three** |

Both searches returned `Shapes.Drawing/Drawing.cs:12`, `Shapes.Drawing/Drawing.cs:14` and `Shapes/ShapeExtensions.cs:6`. The disabled run had **no** `*.assets.cache` path in its worktree notifications either. This establishes parity for the requested find-usages check. Equality of every project-model field, generated symbol and target framework has not been tested. The other eighteen files show why the property cannot promise read-only loading.

Evidence: control output (`cases/control/events.jsonl`), property-set output (`cases/disabled/events.jsonl`), property-set files (`cases/disabled/metadata-diff.json`).

## 4. Process lifetime and ports

| Case | Experiment process tree | After the engine ended |
|---|---|---|
| Normal `exit` sent after `READY` | Engine `dotnet.exe` 2536, `MSBuild.exe` 152480, Roslyn `dotnet.exe` 228648, and three `conhost.exe` processes (225688, 19128, 68312). | Engine exited 0. No observed descendant remained at the first exit sample or at +1, +5 or +15 s. Both listeners were gone by observed exit. The MSBuild log recorded session termination and connection loss. |
| Forced termination of the **engine PID only**, after the indexed query | Engine 174228, MSBuild 230228, Roslyn 145360, and three `conhost.exe` processes (79340, 111816, 164952). | No observed descendant remained at the first post-exit sample (+14 ms) or at +1, +5 or +15 s. Both listeners disappeared within approximately 230 ms of sending the kill. Both worker logs reported connection loss and worker termination. Harness cleanup ran after the observation window. |

The two workers were direct children of the engine, identified by parent PID, process creation time, command line and their own logs. MSBuild ran the private temporary project. Roslyn ran `JetBrains.Roslyn.Worker`. No `VBCSCompiler`, persistent `dotnet build-server` or additional MSBuild node was seen surviving either run. Process polling can miss short-lived processes; it is not a kernel process-start trace. In the earlier interrupted run a short-lived MSBuild process was recovered from its own log.

**Listener identification.** The control engine listened simultaneously on **127.0.0.1:51174** (MSBuild) and **127.0.0.1:51180** (Roslyn). Each worker’s `Creating SocketWire with port` line matches the engine’s listening endpoint, and the OS socket table captured both ends of each established pair. In the kill case the matched ports were **55166** and **59532**. Decompilation of the installed package shows `MsBuildExternalProcess` and `RoslynExternalProcess` both use `ProtocolExternalProcess<TModel,TConnection>`. Its `CreateProtocol` constructs `SocketWire.Server` in the engine, and `RunAsync` creates the protocol before starting the worker. These are engine/worker RD channels, additional to any future Royder MCP endpoint. The initial interrupted run’s port 65163 was the MSBuild channel.

No reserved-port or non-loopback **listener** was observed. The full controls also recorded an established **outbound** TCP connection from the engine to remote port 443; its purpose was not determined. The observation supports budgeting for two concurrent listener ports per host of this fixture. Counts for other language/service combinations remain open.

Evidence: normal process/socket timeline (`cases/control/events.jsonl`), engine-kill timeline (`cases/killed/events.jsonl`), worker logs under normal (`cases/control/state/logs`) and killed (`cases/killed/state/logs`).

## Reproducing the checks

The scripts and full evidence are alongside this report. Use a fresh scratch directory for a new run; the harness intentionally retains each case and refuses to overwrite a worktree. The scripts require the already-installed Python, psutil, pywin32, .NET SDK, Git, PowerShell and `jb`; `dotnet-stack` supplies the optional indexing evidence. No administrator rights or package installation were used.

1. Copy the scripts into the new scratch directory and run `python -B prepare.py C:/Dev/resharper-mcp`. This copies tracked fixture/host inputs, adds a private `bin/` and `obj/` ignore file, creates one private Git commit, restores it and builds the unchanged host in scratch.
2. Run the matched cases: `python -B probe.py control normal` and `python -B probe.py disabled normal --disable-assets-cache`. For the existing-settings variant run `python -B probe.py settings normal --settings-fixture`.
3. For first-attempt removal run `python -B probe.py startupwatch startup`, `python -B probe.py indexcache indexing`, `python -B probe.py idle idle` and `python -B probe.py query query`. The indexing case uses the program’s documented `-` symbol option so a symbol disappearing with its file cannot end the host prematurely. `remove-worktree.ps1` performs ordinary Git removal from a different shell. The parent watcher observes removal from outside the worktree.
4. Run `python -B probe.py causal idle --release-roslyn` to repeat the directory-handle comparison and `python -B probe.py killed killed` to observe an engine-only kill. `directory_handles.py` uses Windows Process Snapshotting and opened disk paths for only the recorded experiment processes. It opens the root for DELETE access without deleting it. `observations.py` collects file metadata and directory notifications.
5. `python -B summarize.py` reads the captured timelines, Git traces, metadata, port lifetimes, remaining directories and settings hashes. `PORT-STOP.json` preserves the original broad-stop observation; the revised harness stops for reserved ports or non-loopback listeners.

**What remains open.** Can Roslyn run with a private working directory while retaining analyzer/source-generator behavior? Would a solution in a nested folder or an imported external project retain additional handles? Can the remaining MSBuild design-time outputs be redirected while preserving ordinary-build parity? The fixture answers the four requested checks for the stock host. I left the failed-removal directories, scripts and evidence in scratch and made no product changes.

## 5. Ways to release the Roslyn worker’s directory

**Keep opening the real solution. Change only the Roslyn child’s working directory through the headless host’s `IProcessStartInfoPatcher`.** The installed 2026.3.0.1 assemblies have a hook at exactly the right point. This preserves solution identity, shared settings, source generators and analyzer support. This section is based on inspection of the installed binaries, Royder’s source, and the existing control run. The proposed replacement has not been composed or run.

### 5a. Where the component can be replaced

| Point in the launch | Verified behavior and cost |
|---|---|
| `RoslynExternalProcessFactory.Create` | The factory is a sealed `[SolutionComponent]`. Its nonvirtual `Create` constructs `new RoslynExternalProcess(..., processInfoPatcher, ..., solution.SolutionFilePath, ...).Run()`. The manager takes the concrete factory. `RoslynExternalProcess` is also sealed. Registering a second process class cannot intercept this `new`. Replacing this part of the chain would entail taking over more of the vendor’s launch and lifecycle machinery. |
| `ProtocolExternalProcess<TModel,TConnection>.GetProcessStartInfo` | Roslyn sets `ProcessStartInfo.WorkingDirectory` to `solutionPath.Directory.FileAccessPath` and returns. Its base then copies that object into `JetProcessStartInfo` and calls the **injected** `IProcessStartInfoPatcher.Patch(info, request)` before `CreateProcess` starts the child. `IRoslynWorkerEnvVariableMutator` contributes environment variables only. |
| `CommandLineSolutionProcessStartInfoPatcher.Patch` | The installed CLI implementation is public, unsealed, registered with `[SolutionInstanceComponent(Instantiation.DemandAnyThreadSafe)]`, and its `Patch` override remains virtual. JetBrains itself derives it from `SolutionProcessStartInfoPatcher`. The platform catalog has inheritance-based leaf selection and explicit `IHideImplementation<T>` support. A Royder subtype loaded into the **headless solution-instance catalog** is the narrow candidate. Merely referencing its assembly will not establish which implementation the container injects. |

Identify the **exact bundled `JetBrains.Roslyn.Worker.exe` path on input**, while it still denotes the worker. The stock patcher may turn that into `dotnet exec` and select a runtime configuration. For that one input, clone the launch settings, change the working directory on the underlying `ProcessStartInfo` exposed by `ToProcessStartInfo()`, then delegate to the stock CLI patcher. Delegate every other input unchanged. This keeps the worker executable, RD port, quoting, runtime selection, environment and redirections on JetBrains’ existing path. Use a dedicated directory inside the host’s private state and outside the worktree.

The cost is one headless component, its catalog registration and a compatibility check per JetBrains wave. The first acceptance check should capture the concrete patcher selected and the actual child’s working directory. Also check MSBuild’s working directory and that a project analyzer or source generator that reads relative files still obtains its intended inputs. The hook affects multiple kinds of process launch, so matching a generic `dotnet.exe` would be too broad. There is no need to change the Rider plugin or the swappable tools assembly for this proposal.

### 5b. Opening a solution in the private host directory

A private solution naming the real projects by absolute path would give Roslyn a private current directory under the observed launch rule, assuming the chosen solution format and loader accept those paths. It would also give **ReSharper and MSBuild a different solution identity**. Three consequences matter:

1. **Team-shared settings.** `SolutionSettingsStorageShared` uses `ISolutionSettingsFileLocationProvider`. Its default provider follows the loaded solution file. `SolutionElement.GetSolutionSettingsStoragePath` derives the adjacent settings filename from that file, including a fallback from `Name.slnx.DotSettings` to `Name.sln.DotSettings` when the former is absent. Both lookups would move to the private directory. The original team layer could be mounted read-only via `CommandLineToolCustomSettingsStorages`, or mirrored with change tracking. A custom profile has its own mount priority; a copy can become stale and relative layer references can resolve differently. Preserve the effective settings hierarchy and keep the personal layer private. Project-local `.editorconfig` ancestry still follows the real source/project paths.
2. **Royder’s routes and path handling.** `McpServerComponent.cs:40` advertises `solution.SolutionFilePath` and `SolutionRoot.cs:31` walks up from its directory to find `.git`. With a private solution outside Git, the private directory becomes both the solution and workspace root. `Serving/OpenSolution.cs:25`, `:53` and `:66` use that identity to decide whether a solution holds a path and how closely it holds it. Absolute worktree files can be rejected, relative file arguments can be based in scratch, result paths can become absolute, and `Tools/ChangedSinceArgument.cs:39` can run Git in the wrong directory. Advertising the real path alone would fix only the peer-facing part. Local lookups, `get_solution_structure` and the other direct callers of `SolutionRoot.Of(ISolution)` would also need a real-solution/worktree identity.
3. **Build model and maintenance.** Absolute project entries retain the project directories. They do not preserve uses of `$(SolutionDir)`, `$(SolutionPath)`, or paths in solution items and solution-level imports. The private representation would need to track changes to solution configurations, project mappings, dependencies and folders. Verify linked projects, generated files and source locations before accepting it. A renamed or copied solution is also a different cache key. It offers no evidence that the remaining design-time `obj` files would move.

This is a viable fallback if the process hook cannot be composed. It puts more code and more parity tests on every solution just to change one child launch setting.

### 5c. What is lost without the worker, and when it starts again

- **Source generators.** The worker supplies generated documents per project and target framework. `RoslynSourceGeneratorDataProvider` feeds those documents into ReSharper’s project model, while `RoslynSourceGeneratedFileManager` exposes generated-file paths and text. A missing generated member can affect symbol lookup, references, diagnostics and refactoring across ordinary source files. `CommandLineRoslynMonitor.Completed` explicitly waits for both Roslyn readiness and up-to-date generator output. The CLI has a legacy, SDK-dependent path for generated files; disabling the worker in a modern project does not automatically activate that path or prove equivalent results.
- **Analyzers and fixes.** `IRoslynAnalyzersFacade` supplies diagnostic highlightings and quick-fix descriptors for a project, framework and file. Its analyzer contributions can disappear when the worker is absent. ReSharper’s own PSI finder and native daemon stages are separate. A simple search may still work; the three-line `IShape.Area` result with Roslyn enabled is insufficient evidence about generator-dependent symbols or analyzer diagnostics with it disabled.
- **Crash behavior.** `RoslynExternalProcessManager.HandleEndSession` flags unexpected termination; `HandleUnexpectedTermination` logs the exit, clears the current PID and sets `WorkerExitedUnexpectedly`. Neither method schedules a restart. In the causal run the host reported the unexpected exit at 02:35:42 local time and remained alive another **5.23 s** without an observed replacement. `RoslynWorkerHost` starts an enabled worker at construction, responds to enablement and runtime-setting changes, and exposes `StartProcess(bool)` for an explicit start. Thus a subsequent trigger can start a worker, but there is no immediate crash-restart loop in the inspected stock command-line path. The control was a short observation after Git had removed the solution’s contents.
- **Deliberate reduced mode.** `RoslynEnabledHook.Disable(Lifetime)` is an actual platform hook. Ending that lifetime can re-enable Roslyn when it is otherwise allowed. This is a plausible explicit option for workloads known to need only native ReSharper services. Killing the worker leaves a recorded failure and gives a caller no reliable account of lost results.

### 5d. Other options and the decision test

A pre-removal stop remains useful for worktrees the hub removes itself. It cannot protect a plain Git removal from another shell. Changing the parent’s working directory has already been ruled out by the causal experiment. A durable vendor-side improvement would be for `RoslynExternalProcess` to accept a host-provided working directory; it could keep the solution directory as its current default. That would remove the need for the headless host’s worker-path check.

**Try the process patcher first.** In a disposable worktree, require a real generator’s member to resolve and a known Roslyn analyzer diagnostic/fix to remain available. Verify the worker has the private directory as its current directory, MSBuild still loads the same projects, the real solution’s team setting takes effect, and an ordinary `git worktree remove` succeeds on its first attempt while Roslyn is still running. Passing only the directory check would establish that the lock moved; these additional observations would establish that the fix preserves Royder’s purpose.
