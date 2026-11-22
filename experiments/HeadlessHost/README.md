# Headless ReSharper host

A .NET 10 console process can open a restored solution with the installed JetBrains command-line tools and answer repeated `IFinder.FindReferences` queries. On Royder, every file and line for `ToolBatch.Of` matched Rider. The two constraints for worktrees are that external edits need a filesystem commit, and that opening a project can write MSBuild intermediates even with `--no-build`.

## Run the experiment

From the Royder worktree root in PowerShell 7 on Windows, with the .NET 10 desktop runtime, a compatible SDK and the `jb` global tool version **2026.3.0.1** installed, run

```powershell
pwsh -NoProfile -File .\experiments\HeadlessHost\Run.ps1
```

The script builds only the experiment. The analysed solution must already have its restored dependencies. It prints the number of projects, the process measurements and the usages of `ToolBatch.Of`, then closes the solution. At Royder revision `496f657` the expected result is `USAGES ToolBatch.Of count=11`. Each location is relative to the solution directory.

- `-Listen` keeps the solution open. At `READY`, enter `ToolBatch.Of` for a repeat query, `refresh` to commit pending filesystem changes, or `exit` to close.
- `-Solution <path> -Symbol Type.Member` selects a **writable** solution and a uniquely named source member. Use `-Symbol '-'` to measure indexing alone.
- `-Cache <path>` selects the state directory. An unused path gives a cold ReSharper start. Repeat with the same path for a warm start. `-JbHome <path>` selects a different installation folder of the same tool version.

## How the engine starts and stops

These calls were traced in the installed binaries with Sherlock and exercised by this experiment.

1. `CommandLineProductsProgram.Main` in `JetBrains.CommandLine.Products.dll` is the `jb` dispatcher. It launches `dotnet exec` on `inspectcode.exe` with the product runtime configuration. `InspectCodeProgram.Main` uses `CommandLineProgramWithPlugins<InspectCodeSettingsData>.Run<IInspectCodeConsoleEnvironmentZone>`. That code starts an STA thread on Windows, supplies the product name and the plugins mixin, joins the thread and returns its exit code.
2. `CommandLineProgram.Prepare` constructs the console `JetHost`, discovers deployed package metadata alongside the product assembly, sets its host information and selects an environment zone. `IInspectCodeZone` requires project model, solution host, daemon, solution-wide analysis, RD and Roslyn support. InspectCode's normal console zone also admits the zone marker for `InspectCodeProductMain`. The host composes the environment and shell.
3. `InspectCodeProductMain.Main` calls `CommandLineProductMain.Main`. The latter waits for shell initialization, calls `SolutionHostManager.OpenSolution`, pumps the dispatcher through project load and synchronization points, and commits PSI documents. With `--no-build` and no explicit restore request it skips its NuGet restore and solution build branches. Its ordinary batch mode disables `IFileSystemTracker` before the cache wait. On return, its disposable owns solution closure.
4. InspectCode runs its inspection pass and disposes the open solution. `RunsPublicStaticIntMain` collects the result of the attributed product entry point and requests shutdown. The main message loop returns the verdict's exit code.
5. Our executable uses `CommandLineProgram.Prepare<IInspectCodeEnvironmentZone>` with `typeof(InspectCodeProgram).Assembly` as the package anchor. This selects the parent zone and leaves the inspection pass out. We call `CreateDontRunAsync`, pump until `ShellContainerComposed` is available, then use the same `CommandLineProductMain.Main` with `isLiveSession: true`. The host owns the dispatcher and keeps the solution disposable alive until the last command. `dotnet exec --depsfile <jb>/inspectcode.deps.json` resolves the installed binaries in place. The executable uses its own .NET 10 runtime configuration because the product configuration's `LatestMajor` selected .NET 11 here.

## Measurements

Measured on Windows x64 on 2026-11-22 with .NET 10.0.15 and Visual Studio MSBuild 18. Each row is one fresh engine process. Elapsed time starts at OS process creation and ends when the shared loader has completed and `IPsiCachesState.IsIdle` is true. It excludes compilation of this experiment and the first query. Cold means empty ReSharper solution **and shell** cache directories. The OS file cache and the existing NuGet restore were available in both runs.

| Solution | Cache | Indexed in | Private bytes | Working set bytes | First query |
| --- | --- | ---: | ---: | ---: | ---: |
| Royder, 5 projects | Cold | 13.177 s | 1,173,147,648 | 1,337,253,888 | 11 lines |
| Royder, 5 projects | Warm | 14.192 s | 990,945,280 | 1,175,490,560 | 11 lines |
| Compze private copy, 125 projects | Cold | 46.320 s | 900,644,864 | 1,137,680,384 | 6 lines |
| Compze private copy, 125 projects | Warm | 49.072 s | 704,131,072 | 971,427,840 | 6 lines |

All 125 project files loaded from a private copy of `Compze.AllProjects.slnx` at `80983a778`. We copied the checkout's source, project inputs, and existing `bin` and `obj` directories into the experiment's ignored `obj` directory. We omitted Git, IDE and agent state, package-feed contents and test results and rebased absolute checkout paths in the copied generated text files. Both runs found the same six lines for `TessageInspector.AssertValidForSubscription`. The actual Compze checkout was monitored during the copy runs and showed no file changes.

**Memory beyond the engine matters.** A third warm Compze-copy run took 35.577 s. An external sample immediately after `INDEXED` counted 1,272 MiB private across the engine, one MSBuild process, one other .NET worker and two console hosts. The engine itself reported 695 MiB private. Their summed working sets were 1,695 MiB, which can count shared pages more than once. All five processes exited. These are point samples. Startup latency varied on this shared machine, and these runs do not establish a typical warm-cache speedup or a peak-memory budget.

## Repeated queries and external edits

`UsageSearch` resolves the member from the solution symbol scope, enters its compilation context, and uses `IFinder.FindReferences` on the solution search domain under a shell read lock. Like Royder it reports one result per distinct file and line. The independently running Rider, queried with `solutionName="royder-codex-2"`, and the headless process returned identical locations in 11 files.

In one warm process, the first query took 1,115 ms and the second took 34 ms. While it waited for commands with the dispatcher running, we inserted an additional real invocation in the existing `ToolBatch.cs`. The headless search stayed at 11 after waits of 0.5, 2 and 10 more seconds. Its `CommandLineFileSystemTracker` reported both `Enabled` and `AutoCommitAllowed`. After `refresh` called `IFileSystemTracker.CommitChanges()` on the guarded main thread, the next query returned 12, including the new line. Restoring the original bytes and issuing `refresh` returned it to 11 in the same PID. This verifies an explicit commit for an existing source file. New files, renames, project changes and the earliest safe query immediately after a commit still need tests.

## What gets written

| Location | Observed contents |
| --- | --- |
| `<Cache>/solution-caches` | `CltSolutionCaches` puts the ReSharper database under the directory from `--caches-home`. Its name contains the solution name and a hash of the full solution path. The two measurements used different solution roots. |
| `<Cache>/local` | Per-host shell caches under `Transient/HeadlessHost/v263/ShellCaches`, plus a usage-statistics database. The launcher sets `JET_LOCAL_APPDATA_DIR`. |
| `<Cache>/roaming` | `HeadlessHost/v263/CultureOptions`, device and user ID files and the event-log salt. The launcher sets `JET_ROAMING_APPDATA_DIR`. |
| `<Cache>/temp` and `<Cache>/logs` | Assembly shadow copies and MSBuild and NuGet temporary directories under `TEMP` and `TMP`. `RESHARPER_HOST_LOG_DIR` directs worker logs to `logs`. |
| Target project's `obj` | SDK project loading can update `*.assets.cache`. On the real Compze checkout a Windows directory-change monitor detected `src/SolutionStructure/obj/Debug/net11.0/Solution.UserDocs.assets.cache` changing and stopped the process tree immediately. Git status was empty before and after. This is why there is no indexed measurement from that checkout. |

The first feasibility run used the default JetBrains local and roaming roots for shell state and settings. It left about 47 MiB in `HeadlessHost` subdirectories in both roots and `Transient/HeadlessHost` in the local root. Later measurements redirected these roots. The launcher builds into its own `bin` and `obj`. In the Royder checkout, git status was clean before the spike and showed only experiment files afterward. The rechecked `ToolBatch.cs` was byte-identical to its starting state. Installed `jb` files showed no modified timestamps during the runs.

**Design implications and remaining questions.** An on-demand host can reuse this solution lifetime and search engine. A worktree budget needs to include child processes. Transparent read-only loading of arbitrary MSBuild projects still needs a design. The SDK's `ResolvePackageAssets` target accepts `DisablePackageAssetsCache`, a candidate for the observed intermediate write, but it gives no general guarantee about other targets. This experiment measured semantic read queries. It did not exercise the rest of Royder's tools or its transport.
