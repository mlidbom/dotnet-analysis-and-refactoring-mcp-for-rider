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

## Serving the unchanged Royder plugin

```powershell
pwsh -NoProfile -File .\experiments\HeadlessHost\Run.ps1 -Royder -Listen -Symbol '-'
```

This serves the worktree's `ReSharperMcp.sln` on loopback port **23910** until `exit` or end of input. The launcher builds the existing host and tools into the experiment's `obj/package-build`, packs only the host DLL in a local Wave 263 package and supplies a private tools build with its matching `host.sha256`. The live program uses InspectCode's `CommandLinePluginsMixin` for package discovery. Both `/health` and `list_solutions` answered with this checkout's solution, indexing complete and 25 tools. The built-in `document <absolute path>` command reads the engine's actual project document without invoking Royder's disk sync. `refactorings` reports the active zones and the two language helpers used by the rename and namespace-move workflows.

### Removing a target worktree while its Roslyn worker runs

`Run.ps1` loads `Royder.Headless.RoslynPatcher` by default, including runs without the HTTP plugin. With `-Royder`, it loads two separate local NuGet packages from the same private feed. Each package is pinned to a version derived from its DLL contents. The live host uses its cache directory as its working directory. The patcher starts the bundled Roslyn worker in `<Cache>/temp/RoslynWorker`. The engine environment sets `JETBRAINS_NO_CHECK_FOR_UPDATES=1`. [Patcher implementation and original measurements](../HeadlessRoslynPatcher/README.md) · [InspectCode extension selection](https://www.jetbrains.com/help/resharper/InspectCode.html)

For a removable worktree, run the host binaries from a separate controller checkout and keep `-Cache` outside the worktree being removed. The default cache is in the controller checkout's experiment directory. Running binaries, caches or a caller's own shell inside the target can independently keep it open on Windows.

```powershell
python -X utf8 experiments/HeadlessHost/Check.py worktree-removal
```

The Windows check requires Python 3.11+ and `psutil`. Use `remove-indexing` or `remove-idle` to run just one phase. Each phase makes a private repository and a detached worktree from the three-project Shapes fixture. The target contains the generator/analyzer DLL and 320 source files providing an observable cold-indexing interval. The engine, its state, the Git caller and the evidence are outside that target.

- **Indexing.** The checker waits for the target solution on port 23910 to report `indexing=true` and independently observes a live Roslyn descendant with the expected working directory. In the final run, plain `git worktree remove` exited 0 in 1.277 s, finishing 3.968 s before the host's `INDEXED` timestamp. Roslyn PID 54484 was running when Git started and logged setup complete later.
- **Idle.** The checker verifies that the solution selected `HeadlessSolutionProcessStartInfoPatcher`, the Roslyn worker logged setup complete, `find_usages` resolved the generator-only `RoundThreeGenerated.Twice` to its call in `RemovalProbe.cs`, and `/health` reports indexing idle. Git exited 0 in 0.683 s on the first attempt with Roslyn PID 235744 alive across removal.

Both removals had the same engine and Roslyn worker alive immediately before and after. The checker issues exactly one noninteractive Git removal, verifies that the directory and its Git registration disappear, then ends the host and verifies both process exits. The JSON-lines evidence includes timestamps, Git's exit code and output, worker identity and cwd. A missed indexing window is an inconclusive run and fails the check. Worktree removal and automatic host shutdown are separate lifecycle behaviors.

### Reproducing the HTTP checks

```powershell
python -X utf8 experiments/HeadlessHost/Check.py all
```

To compare the two ReSharper backends with the same `ReSharperMcp.sln` open in the headless host and everyday Rider, run

```powershell
python -X utf8 experiments/HeadlessHost/Check.py all --paired
```

Replace `all` in either command with `health`, `content`, `add`, `delete`, `move-file`, `rename`, `fix-usings` or `move-type` for a single check. The script uses Python's standard library. `health` always opens the actual worktree solution. By default the other cases use `checks/fixture/LiveProbe.slnx`. With `--paired` they use a uniquely named directory in `tools/DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment`. Each case starts a fresh headless engine, checks the registered solution and the selected host and tools DLLs, then shuts it down.

The paired check prepares all sources outside the project, saves their exact bytes in a private Git index and publishes the directory with one rename. It builds DevelopmentEnvironment before the probe and after a completed probe. Only the headless MSBuild process receives `ROYDER_HEADLESS_IMPORT_PROBE` for the `fix-usings` case. Its method uses the unqualified type in that branch. Ordinary builds and Rider use the fully qualified type, so they compile while the import is missing. The check verifies that headless sees the unresolved type and ordinary Rider sees zero diagnostics before issuing the edit.

On exit, including an exception, the check withdraws the whole directory, restores the saved sources with Git and verifies them byte for byte before removing the scratch directory. An injected failure after changing a source file exercised this path. It left the probe path absent and its Git status empty.

The edit checks preload the actual Rider frontend documents through its native `read_file` with `rootFolder` set to the worktree. Its `ReadToolset` uses `FileDocumentManager.getDocument` and `Document.getText`. The checks compare complete text, normalizing line endings and BOM. Paired runs also read the files through the ordinary Royder backend before and after the edit and check for disk-sync notes. All mutations go to port 23910. Each evidence directory under `obj/live/evidence` contains the console, requests, replies, timings and snapshots. A completed probe records an observed limitation as a result. A setup or protocol failure exits with an error.

### Activating code editing and external sources

**The installed InspectCode host can activate `ExternalSourcesZone`. Its license checker denies `ICodeEditingZone`.** All four runs kept `IInspectCodeEnvironmentZone`. The optional local package adds an activator DLL alongside the unchanged Royder host. The platform invokes its `IActivateDynamic<TZone>.ActivatorEnabled()` callback before composing the shell. The driver checks the recorded votes, then reads the outcome from `RunsProducts.ProductConfigurations.RunningZones`. [JetBrains SDK zone activation](https://www.jetbrains.com/help/resharper/sdk/Activation.html) · [JetBrains SDK component filtering and licensing](https://www.jetbrains.com/help/resharper/sdk/Implementation.html)

```powershell
python -X utf8 experiments/HeadlessHost/Check.py zones
python -X utf8 experiments/HeadlessHost/Check.py zones --request-zones editing
python -X utf8 experiments/HeadlessHost/Check.py zones --request-zones external-sources
python -X utf8 experiments/HeadlessHost/Check.py zones --request-zones both
```

| Zones requested | Running zones | `ICodeEditingZone` | `ExternalSourcesZone` | `get_symbol_source` for `Newtonsoft.Json.JsonReaderException` |
| --- | ---: | --- | --- | --- |
| Neither | 56 | Inactive | Inactive | Null source |
| Code editing | 56 | Inactive | Inactive | Null source |
| External sources | 60 | Inactive | Active | C# metadata declaration |
| Both | 60 | Inactive | Active | C# metadata declaration |

Enabling external sources also activated `NavigationZone`, `ResultListImplZone` and `SymbolsImplZone`. Royder returned the class declaration and member signatures. Method bodies require acceptance of the decompiler notice in the applicable settings.

During shell composition, `RunsProducts.CollectModuleZonesForShell` collects both the requested activations and the zones excluded by the license, preference and expiry checks. For code editing, `ZoneLicenseChecker.IsZoneAllowed` returned **false** for the zone itself in all four runs. `ZoneUserPreferenceChecker.IsZoneExplicitlyDisabled` returned null and the expiry filter allowed it. The external-sources zone was license-allowed throughout. These are the free InspectCode host's decisions on this machine. Using an existing paid subscription in a separately licensed headless host remains unverified. [JetBrains InspectCode documentation](https://www.jetbrains.com/help/resharper/InspectCode.html)

With both zones requested, C# rename and namespace move still selected `RenameHelperBase` and `MoveToNamespaceHelperBase`. Both returned `IsLanguageSupported = false`. The three diagnostics in `McpShellComponent.cs` (`ConvertClosureToMethodGroup`, `UseUtf8StringLiteral` and `AssignNullToNotNullAttribute`) still had no quick fixes. The engine logged non-explicit universal compilation-context warnings during the read probes. The table reports the actual HTTP replies.

We repeated all three real edits on paired DevelopmentEnvironment sources. `rename_symbol` stopped at `UnsupportedLanguage` in 0.078 s. `move_type_to_namespace` stopped at workflow initialization in 0.055 s. Both retained the original declaration and caller. `fix_usings` saved the import in 1.047 s, headless diagnostics went from two to zero, and the preloaded Rider frontend matched disk after 2.436 s. The project built before and after each case, and Git restored the probe files. To repeat, run `Check.py rename --paired --request-zones both`, substituting `fix-usings` or `move-type` for the other edits.

### Which product can use a real license?

**The installed ReSharper for Visual Studio registers account, server and user-key sources. The installed InspectCode product registers none of them.** A paid subscription can authorize a product that supports those sources. The standalone experiment still runs in InspectCode's product context.

This was an offline inspection of shipped binaries and their accompanying XML documentation on 2026-11-22. No product was launched for the inspection. No personal license, account settings or credential store was opened. The earlier live probe established the separate zone decision.

| Installed product boundary | Evidence |
| --- | --- |
| `jb` 2026.3.0.1 | In `inspectcode.exe`, `InspectCodeLicensedEntity` initializes its account and server collections empty and leaves its user-license and evaluation sources null. Neither `InspectCodeSettingsData` nor the common `SettingsData` exposes an account, license-server or activation-file option. The paid `JetBrains.ReSharper.Product.dll` is absent from this distribution. |
| ReSharper for Visual Studio 18 | In `JetBrains.ReSharper.Product.dll`, `ReSharperLicensedEntity` registers a ReSharper-specific account source and server source and implements `ISupportsUserLicense`. The installation on this machine is under `%LOCALAPPDATA%\JetBrains\Installations\ReSharperPlatformVs18_fb906655`. These are installed product files. |
| Rider 2026.3.0.1 | The frontend's `intellij.platform.cs.jar` contains the **Manage Licenses** dialog with JetBrains Account, activation code and license server choices. Its `LicensingFacade` exposes the running IDE's license status and expiration. The separate .NET `RiderLicenseEntity` in `lib/ReSharperHost/Rider.Backend.dll` has empty account/server collections and no user-key source. Rider's product directory is `%LOCALAPPDATA%\Programs\Rider` and its ordinary per-user configuration root is `%APPDATA%\JetBrains\Rider2026.3`. |

| Source | Where it lives and how it is used | When it goes online |
| --- | --- | --- |
| JetBrains Account | The subscription belongs to the JetBrains account. Sign-in and product selection belong to the licensed product's UI and its per-user account settings. | In the managed ReSharper account flow, enabling the source or signing in queues a check for account assets. On success, the server supplies the next entitlement-check time. If it supplies no time, `AdjustNextCheckTime` selects a delay between one and two hours. This describes the .NET account model; I did not verify that Rider's JVM uses the same cadence. |
| License server or License Vault | The organization operates the service and supplies its URL. ReSharper's Windows client persists server selection in its shared per-user JetBrains registry settings (`HKCU\Software\JetBrains\Shared\vAny`). Rider provides a server-address field. Toolbox Enterprise can also supply a candidate server URL. | The client obtains a ticket, prolongs it while in use and has a release operation. For the managed vault flow, `ProlongationPeriodStrategy.GetNextDelay` uses the server-provided ticket period in milliseconds, subject to a client minimum. Rider's bundled ticket-response API also carries a prolongation period. The actual server and its policy were not queried. |
| User key or activation-code file | ReSharper for Visual Studio has a user-key entry and per-user Windows registry settings for that selection. Rider accepts a product-specific activation code by paste or file drop. A downloaded input file lives wherever its owner saved it. | ReSharper checks an added key locally and can recheck stored license data when the product model changes. The implementation also has key-exchange and revocation-data paths. I could not establish a recurring online interval for a locally accepted code, whether any particular subscription offers one, or an indefinite offline guarantee. |
| Running Rider or Toolbox | Rider's license status belongs to its running application. Toolbox 3.9.1.89198 has its own account provider, license-information downloader and product-license inventory. The managed platform documents shared login identity with IDEA/Toolbox. | Toolbox's account-license provider observes the application refresh state and can download an updated inventory. That cadence is separate from IDE entitlement validation. I found no supported interface by which InspectCode borrows the running IDE's seat or turns Toolbox's inventory into a code-editing grant. |

The managed account model's scheduling evidence is in `JBAcountLicenseViewSubmodel.FetchAssetDataAfterSuccessfulLoginAndUpdateState`, `ValidateAssets` and `AdjustNextCheckTime` in `JetBrains.Platform.Shell.dll`. The floating-ticket evidence is in `ILicenseVaultClient`, `HeldTicket.ProlongationPeriodMs` and `ProlongationPeriodStrategy`. `ToolboxLicenseServerUrlSource.GetCandidateUrls` reads a server address. Toolbox's own inventory interfaces are in `licensing-api.jar` and `licensing-jba.jar` under its installed `bin/lib`. The installed code establishes source capabilities and scheduling policies; it does not identify Magnus's selected source or provide a network trace.

**Decision for Magnus.** Using Royder inside an already licensed Rider keeps activation with that product. Keeping our separate executable would require a JetBrains-supported integration that recognizes the real subscription, obtains a grant for `ICodeEditingZone` and owns its renewal. I found no such integration documented or exposed by the installed `jb` product. For an unattended Rider deployment, client licensing and the never-connected case have distinct evidence and open questions. [Remote-development licensing findings](../../dev_docs/headless-royder/rider-remote-dev-server.md#2-licensing-and-the-editing-zone)

### Both ReSharper backends on the worktree solution

The fixture-only editing and daemon probes below can also run with Rider closed. The paired measurements in this section used Rider.

One paired run on 2026-11-22 produced the following results. The Rider column describes the read after the headless check. The frontend delay for `fix_usings` was measured separately by polling the document that was read before the edit.

| Probe in DevelopmentEnvironment | Headless Royder | Everyday Rider |
| --- | --- | --- |
| External content change | The first name-only search found the new type in 0.189 s. A second search confirmed the old name had disappeared. | Found the new declaration in the edited file. |
| Added file | The first search waited 10.234 s and missed the type. A tracker commit still missed it. Reloading the project took 0.472 s and exposed the new path. | Found the declaration in the new file. |
| Deleted file | The old type survived the first search. The tracker commit removed the name, leaving one empty project item. Reloading the project took 0.247 s and removed the item. | Found zero declarations. |
| File moved to an existing folder | The first search still reported the old location after 10.140 s. A tracker commit removed it. A 0.252 s project reload made the destination the only location. | Found the declaration only at the destination. |
| `rename_symbol` | Refused with `UnsupportedLanguage` in 0.065 s. The method and caller retained their names. | Frontend and backend documents agreed with disk. |
| `fix_usings` | Saved the import in 0.633 s. The headless file went from two diagnostics to zero. | The preloaded frontend document matched disk after 2.605 s. The backend matched the file and reported zero diagnostics both before and after. |
| `move_type_to_namespace` | Workflow initialization returned false in 0.044 s. The source and caller stayed in place, and the destination was absent. | Frontend and backend documents agreed with disk. |

### Freshness in the standalone fixture

The searches below call `search_symbol` with the type name alone. We loaded the original file's document before each external edit, deletion or move. Times are local observations from 2026-11-22. The waits in the probe distinguish passive updates from an explicit tracker commit or project reload.

| External change | First HTTP search | What made the result current |
| --- | --- | --- |
| Change the type name inside an existing file | The new name appeared in 0.199 s. The old name disappeared. | A document-change event recorded `DiskSync.Reconcile` calling `DocumentOperationsImpl.SynchronizeDocumentContentsWithStorage` and `SynchronizeDocumentContentsWithDiskFile`. Immediately before the call the raw document still held the old text. |
| Add a new file containing a new type | Zero results after 10.102 s. | Additional searches and `IFileSystemTracker.CommitChanges()` still found zero. `reload_projects` on the owning project took 0.565 s. The next name-only search found the type at the new path. |
| Delete a file containing an indexed type | The first search returned the old type and path. It was still visible after 12 s. | After `CommitChanges()` the symbol disappeared by the one-second retry. A direct lookup still found a project item with empty text. Reloading the project took 0.546 s and removed that item too. |
| Move a file to another existing folder of the project | After 10.114 s the result still lacked the destination. Runs returned either the old path or `[generated]:0`. | `CommitChanges()` invalidated the old declaration. Only `reload_projects` (0.320 s) made a single declaration appear at the destination and removed the old project item. |

For a changed or added path, the first frontend-specific failure in the engine log was **`Not bound: RdCall<RdFsRefreshRequest, Unit>`**. `Rescans.Request` catches it. For a new source the subsequent wait consumed ten seconds. `DiskSync` only reports missing project membership for a file named by the caller, so the name-only call returned zero without a warning. For deletion the first missed step is earlier: `DiskSync.Check` skips a path when `File.Exists` is false.

### Real edits

| Tool | Headless HTTP result | Headless document, disk and Rider |
| --- | --- | --- |
| `rename_symbol` | 0.045–0.092 s. `AtomicRenamesFactory.CheckRenameAvailability` returned `UnsupportedLanguage` for the source method. | The declaration and its caller kept their original names. The next search still found the old method. The preloaded Rider documents matched both disk files on the first comparison. |
| `fix_usings` | 0.810–1.549 s. Added the missing namespace for an unambiguous source type. | The import appeared in the headless document and on disk. The next headless error check went from two diagnostics to zero. The preloaded Rider document initially showed the old text, then matched disk after 5.761, 6.336 and 12.120 s of polling on three runs. |
| `move_type_to_namespace` | 0.051–0.055 s. `MoveToNamespaceWorkflow.Initialize` returned false for a valid top-level type. | The type and caller retained their original text. The source path still existed and the destination had no file or project item. Both preloaded Rider documents matched disk on the first comparison. |

The save event in the 0.810-second run recorded `SolutionDocumentTransactionManager.SaveDocumentsAfterModification` → `DocumentOperationsImpl.SaveDocumentAfterModification` → `SaveDocumentInternal`. The command-line engine wrote the updated source as it committed the document transaction, before Royder needed to request a save. Rider subsequently reloaded the file in its separate process.

The headless host reported `ILanguageCSharpZone` active and `ICodeEditingZone` absent. The live rename and move workflows selected `RenameHelperBase` and `MoveToNamespaceHelperBase` respectively. Both returned `IsLanguageSupported = false`. Inspection of the installed binaries showed that rename checks language support during availability checking, and that namespace-move initialization checks each declaration's language helper. The C# refactoring assembly's zone marker requires both zones. Royder's move error suggests a nested type, type parameter or declaration error even when the required language service is missing.

### Editing tools on the fixture

```powershell
python -X utf8 experiments/HeadlessHost/Check.py editing-services --request-zones both --live-daemon
python -X utf8 experiments/HeadlessHost/Check.py daemon --request-zones both
python -X utf8 experiments/HeadlessHost/Check.py daemon --request-zones both --no-swea
```

These commands use only the experiment's SDK fixture. Omit `--live-daemon` from the first command to run the same matrix with InspectCode's stock daemon. Each editing scenario checks the headless documents and disk bytes, closes its engine, restores the sources through its private Git index and removes the temporary directory. `Run.ps1` checks for at least 4 GiB of available physical memory before launch, samples availability while the engine runs and stops its own process tree if a sample is below 2 GiB. It redirects JetBrains settings, caches and temporary files and sets `JETBRAINS_NO_CHECK_FOR_UPDATES`.

Both zones were **requested** for these calls. `ExternalSourcesZone` ran, and `ICodeEditingZone` was denied by InspectCode's license checker. We did not sign in to a JetBrains account, initiate a license-server check or install a license. Testing the licensed editing services still requires a supported product context that grants the zone.

| Tool and case | Observed result |
| --- | --- |
| `rename_symbol`, real and dry run | Both returned `UnsupportedLanguage` before the workflow started. The method and its caller in a second file kept their original names and bytes. The dry run never reached rollback. |
| `move_type_to_namespace` | Workflow initialization returned false for a top-level type. The source and caller stayed in place, and the destination file and project item remained absent. |
| `list_quick_fixes` and `apply_quick_fix` | The `ConvertClosureToMethodGroup` highlighting covered the caret. The list was empty. Applying the known `ToMethodGroupFix` returned `applied=false` and left the source intact. |
| `apply_suggestions` | The same inspection ID in file scope returned zero applied fix types. The lambda remained on disk and in the engine. |
| `generate_members` | Generated a constructor assigning its argument to a writable property. The equality case generated the typed and object `Equals` methods and `GetHashCode`. The generated text was saved. |
| `format_file` | The default formatter corrected whitespace. The full-cleanup and code-style profiles also reformatted the test method and saved it. In the tested profiles, the explicit `private` modifier and `int` local remained. |
| `complete_at` | Returned zero candidates after `this.` where a known instance member was in scope. A direct probe created an `AnonymousTextControl` on the correct document, moved it to offset 196 and read back 196. ReSharper produced a C# completion context and a non-null result. The sole registered provider for that evaluation was `BaseRelevanceRule`, which decorates existing items. |
| `get_symbol_source` on a compiled type | Returned the C# metadata declaration and member signatures of `System.InvalidOperationException`. The private settings retained the decompiler notice setting, so this did not exercise method-body decompilation. |

Both daemon configurations gave the same capabilities in these cases. No frontend was needed for the successful writes. The failed rename, move and quick-fix requests stopped before an interactive step. The installed `JetBrains.ReSharper.Features.Intellisense` namespace requires `ICodeEditingZone`, and its C# completion namespace also requires `ILanguageCSharpZone`. This is consistent with finding a C# context but no member-producing rule. Which interactions a fully licensed composition would require remains open. [JetBrains SDK namespace zone markers](https://www.jetbrains.com/help/resharper/sdk/Zones_Usage.html)

### Choosing the daemon for an editing host

**Use the platform's ordinary `DaemonImpl` and `DaemonThread` for the live host.** `InspectCodeDaemonManual.OnPsiTransactionStarted` calls `Logger.Fail` on a normal PSI transaction, and `InspectCodeDaemonThread` executes queued daemon jobs synchronously. With `-LiveDaemon`, the local Royder package includes `Royder.LiveDaemon.dll`. Its two subclasses select the ordinary implementations through the SDK's `IHideImplementation<T>` mechanism and inherit their behavior with no overrides. The experiment verifies the concrete `IDaemon` and `IDaemonThread` resolved from the solution container. [JetBrains SDK component replacement](https://www.jetbrains.com/help/resharper/sdk/ContainersPartsCatalogues.html)

The comparison opens the same solution and identical fixture sources in each configuration, runs `get_diagnostics` and file- and solution-scope `inspect_code`, applies a real `fix_usings`, verifies the saved bytes and repeats both inspections. The edit produced byte-identical files. Only the manual daemon logged **Unexpected transaction in command line**. The full `get_diagnostics` JSON matched before and after the edit.

| Solution-wide analysis | `inspect_code` outcome |
| --- | --- |
| Enabled | The manual daemon still reported incomplete after each 15 s wait. The live daemon completed analysis. Its reports included global unused-member and unused-type findings. One pre-edit sample also differed on two Roslyn `CA1822` hints. The reports at these different readiness states differed. |
| Explicitly off in both engines | File results matched at 10 findings before the edit and nine afterward. Whole-solution results matched at 13 before and 11 afterward. Each comparison includes file, position, severity, ID, message and multiplicity. The raw text could order a Roslyn finding differently at a tied position, even after removing elapsed time. |

The raw replies, analysis-readiness samples, concrete service types, issue differences and both daemon logs remain under the printed evidence directory. Full solution-wide parity when both daemons have completed remains unverified. The fixture timings do not establish large-solution behavior.

### Smallest proposed changes

- Keep the existing document reconciliation for a headless content change. Skip the Rider-specific refresh request when the host has no frontend. For structural changes, retain missing paths through `DiskSync.Check`, commit the engine tracker, reload each affected project once and verify the old and new project items before answering. The existing `reload_projects` implementation supplies a tested reload and index wait. This also avoids spending ten seconds waiting on an unbound frontend. [DiskSync.cs](../../src/DotNetAnalysisAndRefactoringMcp.Tools/Disk/DiskSync.cs) · [ReloadProjectsTool.cs](../../src/DotNetAnalysisAndRefactoringMcp.Tools/Tools/ReloadProjectsTool.cs)
- For rename and namespace moves, the headless product needs a host licensed for C# code editing. The explicit zone request above confirmed the first blocker. Keep the existing symbol and refactoring checks and report the missing capability directly, especially in `MoveTypeToNamespaceTool`. The later steps of these workflows remain untested in a licensed headless host. [RenameSymbolTool.cs](../../src/DotNetAnalysisAndRefactoringMcp.Tools/Tools/RenameSymbolTool.cs) · [MoveTypeToNamespaceTool.cs](../../src/DotNetAnalysisAndRefactoringMcp.Tools/Tools/MoveTypeToNamespaceTool.cs)
- `fix_usings` needs no persistence change for the tested file. Selecting the ordinary daemon and daemon thread removed InspectCode's transaction error in the controlled comparison above. The experiment left production Royder unchanged.

Changes to project references and generated files, refactoring rollback and edits racing another writer remain open. The failed rename and namespace move did not reach their mutation or save paths.

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
