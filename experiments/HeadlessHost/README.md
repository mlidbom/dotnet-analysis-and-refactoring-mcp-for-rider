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

| Solution | Local start | Cache | Indexed in | Private bytes | Working set bytes | Machine CPU | Peak run queue |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| Royder | 02:09:26 | Cold | 13.177 s | 1,173,147,648 | 1,337,253,888 | 51% | 21 |
| Royder | 02:09:55 | Warm | 14.192 s | 990,945,280 | 1,175,490,560 | 61% | 27 |
| Royder | 02:12:09 | Warm | 17.813 s | 1,023,541,248 | 1,201,954,816 | 80% | 88 |
| Royder | 02:19:22 | Cold | 13.462 s | 1,192,849,408 | 1,333,915,648 | 43% | 0 |
| Royder | 02:20:31 | Warm | 18.550 s | 1,009,254,400 | 1,174,450,176 | 89% | 152 |
| Royder | 02:21:11 | Warm | 11.177 s | 1,010,778,112 | 1,174,069,248 | 42% | 0 |
| Compze copy | 02:06:07 | Cold | 46.320 s | 900,644,864 | 1,137,680,384 | 83% | 159 |
| Compze copy | 02:07:31 | Warm | 49.072 s | 704,131,072 | 971,427,840 | 89% | 226 |
| Compze copy | 02:10:46 | Warm | 35.577 s | 728,272,896 | 980,881,408 | 68% | 70 |
| Compze copy | 02:17:26 | Cold | 42.242 s | 950,280,192 | 1,160,351,744 | 72% | 83 |
| Compze copy | 02:18:10 | Warm | 45.694 s | 719,720,448 | 977,063,936 | 89% | 172 |
| Compze copy | 02:21:47 | Warm | 28.191 s | 729,964,544 | 1,004,650,496 | 41% | 1 |
| Compze copy | 02:22:46 | Cold | 39.815 s | 938,078,208 | 1,146,720,256 | 78% | 62 |

The machine stall recorder reports ten-second intervals. Machine CPU is the average across the startup window, weighting each interval by its overlap. It includes the engine and all other processes. Peak run queue is the largest sampled number of ready threads in the overlapping intervals. The short Royder runs span only a few intervals. To inspect a run on this machine, use its local start from the `INDEXED` line, for example `ClaudeTooling.exe machine-stalls at "2026-11-22 02:07:31" --minutes 2`. An additional warm Royder run at 02:19:38 returned 11 lines, but its captured output lost the measurement line. The surrounding ten-second intervals averaged 44–62% CPU with a peak queue of one. The last two Royder rows are its repeats.

During the 49.072-second warm Compze run, ten-second average CPU reached 99.6%. `ClaudeTooling.Specifications` alone occupied as many as 8.6 processors. Compze later indexed warm in 28.191 seconds with just one ready thread at the busiest sample. For Royder, warm startup ranged from 11.177 seconds at 42% machine CPU to 18.550 seconds at 89%. These runs show how much startup varied with machine load. They cannot isolate the contribution of cache reuse.

All five Royder projects and all 125 Compze projects loaded. Compze ran from a private copy of `Compze.AllProjects.slnx` at `80983a778`. We copied the checkout's source, project inputs, and existing `bin` and `obj` directories into the experiment's ignored `obj` directory. We omitted Git, IDE and agent state, package-feed contents and test results and rebased absolute checkout paths in the copied generated text files. The second cold/warm pair used a fresh private copy and an empty state directory at the same copy path. Every Compze run found the same six lines for `TessageInspector.AssertValidForSubscription`. The actual Compze checkout was monitored during the copy runs and showed no file changes.

**Memory beyond the engine matters.** In the 02:10:46 Compze run, an external sample immediately after `INDEXED` counted 1,272 MiB private across the engine, one MSBuild process, one other .NET worker and two console hosts. The engine itself reported 695 MiB private. Their summed working sets were 1,695 MiB, which can count shared pages more than once. All five processes exited. These figures measure memory at indexing completion only.

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
