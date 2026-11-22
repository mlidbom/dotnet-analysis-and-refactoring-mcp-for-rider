# Royder read tools in Rider and InspectCode

Of 30 paired cases, 18 matched, 10 differed and two failed headless. On 22 November 2026 both hosts navigated the same five-project solution and completed solution-wide analysis. Three differences matter for a worktree host. InspectCode had no C# metadata translator, offered no quick-fix for a known highlight, and rejected a C# rename preview before its workflow began. A working text-control manager also produced no completion at a position where Rider offered `Warn`.

## Reproduce

```powershell
pwsh -NoProfile -File experiments/HeadlessExtension/Run.ps1 -CompareReadTools
```

The recorded run used Rider on 23751 with `solutionName=royder-codex-3`, and InspectCode 2026.3.0.1 on .NET 10.0.15 on 23900. Rider served `2026-11-19_11-59-23-7628571`. Headless served a byte-identical private copy named `extension-check`. The driver packed the host whose SHA-256 Rider reported and copied that Rider's tools DLL to a private tools folder. The SHA-256 values were `7529fd2e44f63a15a6f0473b681767eea4fbb2591bbe74328433e4ec1d1d7660` (host) and `36f641995417522736825c59cb044e350bec2b1eb62fd2bb6d2252a140bc7292` (tools). Rider kept the same build throughout.

Both hosts received the same JSON-RPC body for each case. **Same** compares returned payloads, decoding JSON-valued tool text and ignoring JSON whitespace and object-key order. For `inspect_code` only, the driver also replaces the elapsed-seconds field in the first line. List order, source line endings, severities, paths, notes and counts are preserved. **Failed** denotes a transport, JSON-RPC or tool error, or a source request returning no source. For completion, an empty list is a valid returned payload. The table states whether it offered a usable completion.

In the evidence directory printed by the command, `comparison/<case>/` contains the request, `rider.json` and `headless.json`. The case IDs below are those directory names. `components.txt`, `assemblies.json`, `zones.txt` and the engine's logs are beside `comparison/`. The run recorded here is `extension-071d98a632ba4adeb2ae7d8f18bf8340` under `%TEMP%/royder-headless/royder-codex-3/`.

## Paired results

| Tool | Case | Result | Evidence |
| --- | --- | --- | --- |
| `tools/list` | `tools` | Same | All 26 entries including descriptions and schemas. Each solution exposes 25 tools. `list_solutions` is the server-level entry. |
| `list_solutions` | `inventory` | Different | Rider listed six solutions and gave this checkout a unique path segment. Headless listed only this checkout. Both gave it 25 tools. |
| `find_usages` | `usages` | Same | One call to `WatchdogPing` in `McpShellComponent.cs:115`. |
| `get_symbol_info` | `members` | Same | With `includeMembers=true`, both returned the abstract `OpenSolution` class at 13:16, its documentation and 15 members. |
| `find_implementations` | `implementations` | Different | Both found `LocalSolution:13` and `PeerSolution:14`. The 200-character excerpts end earlier in headless text because CRLF consumes two characters. |
| `search_symbol` | `search-source` | Same | One class, `McpShellComponent`, at line 16. |
| `search_symbol` | `search-libraries` | Same | With `includeLibraries=true`, both found one `JsonReaderException` in `Newtonsoft.Json.dll`. |
| `go_to_definition` | `definition-text` | Different | Both returned `WatchdogPing` at 87:9. The embedded source uses LF in Rider and CRLF headless. Replacing CRLF with LF makes the answers equal. |
| `go_to_definition` | `definition-json` | Same | One method declaration with matching kind, qualified name, file, line and column. |
| `browse_namespace` | `namespace-text` | Same | `DotNetAnalysisAndRefactoringMcp.Serving` contained eight types, from `LocalSolution` to `ToolList`. |
| `browse_namespace` | `namespace-json` | Same | The same eight qualified type records and source positions, with no child namespaces. |
| `get_solution_structure` | `projects` | Same | All five projects, their frameworks, file counts and project references matched. |
| `list_symbols_in_file` | `declarations` | Same | 21 declarations in `McpShellComponent.cs`. |
| `get_file_errors` | `file-errors` | Same | No diagnostics in `McpShellComponent.cs`. |
| `get_file_errors` | `changed-errors` | Same | Since `2656956`, both checked `RenameSymbolTool.cs`, found no errors, and skipped four experiment sources outside the solution. |
| `flow` | `flow` | Same | `WatchdogPing` had the same null guard, peer promotion branch, ping, exception handler and restart. |
| `get_symbol_source` | `source` | Different | Both returned the `WatchdogPing` declaration from 87:4 to 108:5. The only difference in its source was LF versus CRLF. |
| `get_symbol_source` | `compiled-source` | Failed headless | Rider returned 5,442 characters of decompiled `JsonReaderException`, including bodies. Headless returned the qualified name with `source:null`, `file:null` and all coordinates zero. Its C# metadata translator was absent. |
| `get_call_hierarchy` | `incoming` | Same | `Watchdog` called `WatchdogPing` at line 115. |
| `get_call_hierarchy` | `outgoing` | Same | `TryPromoteToPrimary`, `PingServer`, `Warn` and `Restart` with the same call sites. |
| `get_type_hierarchy` | `bases` | Same | `LocalSolution` extends `OpenSolution`. |
| `get_type_hierarchy` | `subtypes` | Same | `OpenSolution` has the two derived types `LocalSolution` and `PeerSolution`. |
| `get_diagnostics` | `diagnostics` | Different | Same three diagnostics, positions and severities on `McpShellComponent.cs`: `ConvertClosureToMethodGroup` and `UseUtf8StringLiteral` at suggestion, `AssignNullToNotNullAttribute` at warning. Rider set all three `hasQuickFix` flags to true. InspectCode set all three to false. |
| `get_diagnostics` | `editorconfig` | Different | At the unbraced return on line 89, neither reported `IDE0011` or any other diagnostic at hint or higher. Their notes counted 546 raw file highlights in Rider and 92 headless. |
| `inspect_code` | `file-inspections` | Different | On `McpShellComponent.cs` at suggestion or above, Rider returned three warnings (two `MemberCanBeInternal`, one `UnusedMember.Global`). Headless returned four findings (the same unused member at suggestion, plus the three file diagnostics above). Both answers said global inspections were included. |
| `inspect_code` | `solution-inspections` | Different | Both finished all 202 files with SWEA complete. At hint or above, filtered to `UnusedMember.Global`, both found `SkippedElements.Count` and `McpShellComponent.Port` at the same locations. Rider reported warning and InspectCode suggestion for both. |
| `list_quick_fixes` | `bulb` | Different | At 54:54 Rider offered `ToMethodGroupFix` (“Replace with method group”). InspectCode found the highlighting at the same position and returned no fixes. |
| `complete_at` | `complete-environment` | Same | Both returned an empty list and the same unavailable-completion note inside `Environment.GetEnvironmentVariable`. This case establishes no completion capability on either host. |
| `complete_at` | `complete-logger` | Different | At 105:18, immediately after `_logger.`, Rider returned 134 items including `Warn (in JetBrains.Util)`. Headless returned zero items and the unavailable-completion note. |
| `rename_symbol` | `rename-preview` | Failed headless | Rider returned `dryRun:true`, `applied:false`, no conflicts and one affected file, `McpShellComponent.cs`. Headless returned `Symbol 'WatchdogPing' cannot be renamed: UnsupportedLanguage.` The worktree digest was identical before and after each call. |

## Composition and settings

| Service requested in the headless solution | Concrete implementation |
| --- | --- |
| `IDocumentStorageHelpers` | `JetBrains.DocumentManagers.impl.DocumentOperationsImpl` |
| `ISolutionHost` (projects-host container) | `JetBrains.ProjectModel.ProjectsHost.SolutionHost.Impl.SolutionHost` |
| `IPsiCachesState` | `JetBrains.ReSharper.Psi.Caches.PsiCachesState` |
| `ITextControlManager` | `JetBrains.TextControl.TextControlsManagement.TextControlManager` |
| `CodeCleanupSettingsComponent` | `JetBrains.ReSharper.Feature.Services.CodeCleanup.CodeCleanupSettingsComponent` |

`SolutionAnalysisConfiguration.Enabled` and `.Completed` were both true at the headless callback. SWEA was complete on both whole-solution inspection replies. This comparison establishes working file diagnostics and a completed global inspection. The cause of every difference in the unfiltered file-inspection results remains open.

The repository sets `dotnet_diagnostic.IDE0011.severity = none` in its root EditorConfig. Both hosts omitted that diagnostic at the unbraced `if`. It is a suppression-only observation. An absent diagnostic cannot demonstrate that the setting was read, and the two raw-highlight counts do not measure that setting. An independent settings difference is observable in the severity actually returned for `UnusedMember.Global`. The owning settings layer for that difference was not isolated.

Rider's settings store has `DecompilerLegalNoticeAccepted=True` and its tool returned method bodies. Headless had no `IMetadataTranslator` for `CSharpLanguage`, which is the first missing service on the actual source path. A separate direct read of `DecompilerLegalNoticeAccepted` threw `ArgumentException` with “There is no settings key \"JetBrains.ReSharper.ExternalSources.MetadataViewer.DecompilerSettings @ JetBrains.ReSharper.ExternalSources\" in the settings schema.” Supplying consent alone cannot supply the missing translator.

## What explains the gaps

The installed C# metadata translator requires `ExternalSourcesZone`. The C# intentions and refactorings zone markers require `ICodeEditingZone`. Both zones were absent from InspectCode's 57 running zones, although the implementation DLLs ship beside the engine. The C# rename service resolved to `RenameHelperBase` with `IsLanguageSupported=false`. `AtomicRenamesFactory.CheckRenameAvailability` returned `UnsupportedLanguage` before Royder began a transaction. These observations support testing the two zones in the dedicated executable. That composition, a real edit/save, and the precise reason the completion provider returned no items remain untested here.

The changed-file query also exposed a frontend dependency in a read path. Headless logged `Not bound: RdCall<RdFsRefreshRequest, Unit>` when trying to rescan experiment files outside the solution and still returned the same file-error report as Rider. This call took 10.4 seconds headless and 0.4 seconds in Rider. Running `inspect_code` from inside InspectCode's own inspection also logged unexpected write-lock and synchronization-point diagnostics. Those calls completed. These lifecycle observations belong to the InspectCode extension and need a separate check in a live-session host.
