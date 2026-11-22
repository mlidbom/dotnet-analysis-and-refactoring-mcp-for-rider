# Rider and InspectCode settings parity

**For personal settings, use a private global settings file that injects a copy of the personal layer at Rider's priority.** InspectCode 2026.3 mounted `--profile` read-only and let it override the repository's EditorConfig. Injecting the same copy preserved the repository's indent size and still applied an unrelated personal formatting preference.

The personal layer explains the severity of `UnusedMember.Global` and the two `MemberCanBeInternal` findings. Copying it did **not** make every `inspect_code` result match. With the copy, the three Rider warnings appeared headless, along with three file diagnostics that were already in InspectCode's completed solution-wide analysis cache.

## Reproduce

```powershell
pwsh -NoProfile -File experiments/HeadlessExtension/Check-Settings.ps1
```

Use the same Windows setup as the extension experiment, with Python 3 and an everyday Rider open on this worktree. [Setup instructions](README.md) The driver uses identical Royder assemblies on ports 23751 and 23900. It starts and stops one private InspectCode process per case. The `default` case enables the positive EditorConfig check. The driver creates two source copies in a temporary folder under the development-tools project, with a file symlink to the EditorConfig in private Temp. It removes the copies and link when the cases finish. The following PowerShell command runs only the precedence cases and uses existing project files.

```powershell
& ./experiments/HeadlessExtension/Check-Settings.ps1 -Cases @('profile-conflict','injected-conflict')
```

The printed settings evidence directory contains each request, both JSON-RPC answers, `effective.txt`, `mounts.tsv`, and before/after SHA-256 files. The per-case `engine.json` locates the private engine logs. `cached-issues.txt` records the issues already stored for `McpShellComponent.cs` before Royder runs its inspection. The hash check reports any changed protected file and returns an error on a byte mismatch, including a save by the concurrently running Rider.

## 1. Layers and inspection results

| Layer | Rider in this checkout | Private InspectCode |
| --- | --- | --- |
| Product defaults | Rider's backend log reports solution-wide warning mode `ShowAndRun`. | `InspectCodeDefaultSettings` sets `ShowAndRun`, which the probe confirmed at runtime. |
| Global, “This computer” | `%APPDATA%/JetBrains/Rider2026.3/resharper-host/GlobalSettingsStorage.DotSettings`. The backend log records this directory as `RESHARPER_HOST_CONFIG_DIR`, the directory selected by `RiderAnyProductSettingsLocation`. | `GlobalPerProduct`, priority −10, writable, at `<private roaming>/Shared/vAny/GlobalSettingsStorage.DotSettings`. The run redirects JetBrains' app-data roots. |
| Injected personal settings | The global file's `FileInjectedLayer` entry points to `%APPDATA%/JetBrains/Shared/vAny/MyResharperSettings.DotSettings`, with relative priority 1. Its `InspectionSeverities` entries set `MemberCanBeInternal` and `UnusedMember_002EGlobal` to `WARNING`. | Absent from a fresh private root. The experiment supplies it explicitly or injects it below the global mount. |
| Solution and project settings | Neither `ReSharperMcp.sln.DotSettings` nor `ReSharperMcp.sln.DotSettings.user` exists. The five projects also have no corresponding shared or personal files. | The default table still contains solution personal (−1100, writable), solution shared (−1000), project personal (−1300, writable) and project shared (−1200) slots. A slot can name a nonexistent file. |
| Per-file settings | The backend log records the worktree's `.editorconfig`, SDK global analyzer configs and generated MSBuild EditorConfigs. | The mount table records `ExternalConfig` at −1400 for the repository's `.editorconfig` and the generated analyzer configs. |

Rider also has IntelliJ settings providers and extension settings. Its extension manifest lists the Avalonia live/file templates. The severity entries above are in the injected personal file. The similarly named `Shared/vAny/GlobalSettingsStorage.DotSettings` contains the same two severity entries. The running Rider selects its own global file and follows that file's injection to `MyResharperSettings.DotSettings`. [JetBrains' description of Rider settings layers](https://www.jetbrains.com/help/rider/Sharing_Configuration_Options.html)

All file-inspection comparisons below used `McpShellComponent.cs`, `minSeverity=suggestion`, `maxResults=50`, and completed solution-wide analysis. Rider consistently reported unused `Port` at 25:15 and two methods that can be internal at 77:4 and 79:4, all at warning.

| Headless input | Result | What the control establishes |
| --- | --- | --- |
| Fresh private root | 4 | Unused `Port` was a suggestion. The other findings were `ConvertClosureToMethodGroup`, `UseUtf8StringLiteral` and `AssignNullToNotNullAttribute`. |
| Only the two personal severity entries via `--profile` | 6 | `Port` became a warning and both methods appeared as warnings. All three file diagnostics remained. |
| Same entries plus the personal `IncludeWarningsInSwea=true` entry | 6 | The extra entry did not change this result. |
| Full personal copy via `--profile` | 6 | Same findings and severities as the two-entry control. The three extras were present in `SolutionAnalysisManager.IssueSet.AllIssues` before the call. |
| Full personal copy plus `--disable-settings-layers` (`-dsl`) for all six built-in file-layer IDs | 6 | None of those six IDs remained in the mount table. The explicit profile and `ExternalConfig` remained. |

`CollectInspectionResults.RunGlobalInspections` reads the stored issues for eligible files and runs the global-usage pass. The headless probe recorded `IsSwaEnabled=true`, and its stored entries were exactly the two local suggestions and the one nullability warning above. Rider reported those same three findings through `get_diagnostics`, which drives the visible-document daemon, in the paired run. [Paired tool comparison](read-tool-results.md) The personal severity setting explains the change from four to six headless findings. The evidence does not identify a further personal setting that removes the three cached results, or why Rider's solution-wide result omitted them.

## 2. Read-only mounts and formatting precedence

In the installed CLI, `CommandLineToolCustomSettingsStorages` passes `isWritable: false` to `FileSettingsStorageProviderBase`. The explicit file's runtime mount had flags `None` and priority −99999. For a file injected through private global settings, `RegisterInjectedMounts` resolves the relative path against the host **file** path, and the injected copy appeared at its verified full path with flags `None` and priority −9.98.

The control changed the personal copy's C# `INDENT_SIZE` from 3 to 7. The repository retained `indent_size = 3`, and the personal file also says `SPACE_BEFORE_IF_PARENTHESES = False`. The probe read both settings in the context of `McpShellComponent.cs`.

| Mount | Effective C# indent | Space before `if` parentheses |
| --- | ---: | --- |
| Fresh root | Not sampled (repository specifies 3) | True |
| Full unmodified personal copy | 3 | False |
| Conflicting copy as explicit profile | 7 | False |
| Same conflicting copy as global injection | 3 | False |

The unrelated spacing setting confirms the injected layer was active when EditorConfig supplied the indent. The two precedence runs also returned the same six file-inspection findings with solution-wide analysis complete. These are observations of effective settings and read tools. For a production host, mirror the intended layers into private storage and preserve their relative position. In particular, keep `SolutionShared` available when a repository has a team file. The blanket `-dsl` suppression above was an experimental control. [JetBrains InspectCode settings options](https://www.jetbrains.com/help/resharper/InspectCode.html)

**There is no blanket no-write guarantee for a mounted layer.** The profile and injected file are read-only to ordinary settings-store writes. The default global and personal mounts can be writable, and mount flags do not enforce filesystem permissions. Every tested profile copy and the real personal file retained its bytes. The SHA-256 of the real personal file and its unmodified copy was `40071b6e739656c41370c8fa5c83d1817ed845dce5e6c23c24331f08ed9deccb`.

**Live Rider hash exception.** Before the first probe and at the final audit, the same 16 existing DotSettings/EditorConfig files were found across the scanned roaming JetBrains settings and checkout. Fifteen retained their SHA-256. Rider's real `resharper-host/GlobalSettingsStorage.DotSettings` changed. During the instrumented fixture run, its XML entry comparison isolated `/Default/Environment/Hierarchy/GeneratedFilesCacheKey/Timestamp/@EntryValue`, and the Rider backend log records a successful save of “This computer”. The preliminary run had only a whole-file hash, so its exact key delta is unknown. The later profile runs without adding project files had identical before/after hashes. The harness left the live file as Rider saved it. Both solution settings files remained absent.

## 3. Positive EditorConfig check

The two files contained identical C# bytes, including `register(() => disposable.Dispose())`. The scratch EditorConfig set `resharper_convert_closure_to_method_group_highlighting` to `suggestion` in `[Control.cs]` and `error` in `[Raised.cs]`. Both hosts' `get_diagnostics(minSeverity="hint")` returned the named inspection in each file.

| File | Rider | InspectCode |
| --- | --- | --- |
| `Control.cs` | suggestion | suggestion |
| `Raised.cs` | error | error |

## 4. Source line endings

The `WatchdogPing` declaration on disk had 21 CRLF separators. Headless `get_symbol_source` matched that decoded slice exactly. Rider returned the same text with 21 LF separators. Converting CRLF to LF made the strings equal. Git also reported `i/lf w/crlf` for the file.

Royder's `GetSymbolSourceTool` reads the declaration's document range with `IDocument.GetText`. The common `DocumentOperationsImpl.GetDocumentTextFromFile` reads the bytes as text. In Rider, `RiderDocumentOperationsImplCore.GetDocumentTextFromFile` calls the common reader and then `ConvertTextAndCacheInfo`. That method remembers the original line ending for the path and applies `UnifyLineSeparators(LineEnding.LF)`. The conversion happens in Rider's in-memory document before Royder serializes the answer.

## Recorded evidence

The following directories are under `%TEMP%/royder-headless/royder-codex-3/`.

| Directory | Cases used here |
| --- | --- |
| `settings-1d61d6cb9f0f4e1fa10a3f02b957f0fb` | Fresh root, two-key and three-key controls, positive EditorConfig results, live-store hash exception. |
| `settings-ffef77ce314944658e1cf3cfafc688ee` | Full personal profile, suppression of built-in layers, stored issues. Used existing project files. |
| `settings-b612943c299e489198d9e93e5ed92519` | Matched explicit-profile and injected-file precedence controls, including the resolved injection path. Used existing project files. |
