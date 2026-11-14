# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Versions up to 0.12.0 are Joshua Light's MCP Server for Code Intelligence (`com.j-light.resharper-mcp`), which this
plugin is a fork of.

## [Unreleased]

### Changed

- No use of deprecated platform API: the Kotlin compiler no longer generates a forwarding method for the deprecated `StatusBarWidget.getPresentation(PlatformType)` into the status-bar widget, which the Marketplace's verifier reported as two usages of deprecated API

## [0.13.0]

Forked and renamed.

- Forked from Joshua Light's MCP Server for Code Intelligence 0.12.0 as .NET Analysis&Refactoring MCP, with an id, a default port (23751) and a port variable (`DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT`) of its own, so it can be installed beside the original
- Works on Rider 2026.3: every tool call failed there with a `MethodAccessException`, because `AllDocumentsAreCommittedReadConstraint` became internal
- No more "Multiple solutions are open" for calls that name no file when only one solution is open: Rider 2026.3's lightweight backend registered a solution without a file, which is now skipped
- Type headers in `get_symbol_info`, `go_to_definition`, `list_symbols_in_file` and `flow` say `sealed`, `static` or `abstract` as declared, instead of `static` for every type; an interface no longer lists `object` as its base type

## [0.12.0]

Trustworthy after disk edits.

- Files edited on disk (by scripts, `sed`, heredocs) are synced before every call: the server watches the work tree, reconciles stale documents with disk the way Rider's file watcher does, and asks Rider to rescan new files so they join their project. Documents with unsaved edits in Rider are never touched
- Edit tools (`fix_usings`, `format_file`, `rename_symbol`, `generate_members`, `apply_quick_fix`, `apply_suggestions`) refuse while Rider's copy of a file differs from disk, instead of saving old text over a newer edit. Dry runs are never refused
- One element ReSharper cannot read no longer fails a whole call: solution-wide and file-wide walks skip it and say so in one line, and every batch tool isolates its items
- `go_to_definition` and other position-based tools no longer crash near string literals; when resolution still fails, the identifier is resolved by name
- `get_file_errors`: one error per unresolved reference, inspection failures collapsed into one line, one line for a project that is not loaded, `maxDiagnostics` (errors first), and likely source-generator false positives marked as unverified
- `get_file_errors` `changedSince: "<git ref>"` / `changed: true` checks every changed source file in one call
- Symbols compiled into referenced DLLs resolve by name; `get_symbol_source` and `go_to_definition` return ReSharper's decompiled source; `search_symbol` has `includeLibraries`
- Names like `Type.Member`, `Wait<M>`, `Execute(int)` work in every by-name tool, with a case-insensitive fallback
- Errors name the next step: "did you mean" candidates for unknown symbols, whether a missing file exists on disk, and one-line crash reports (the stack trace goes to the IDE log)
- `find_usages` prints rg-shaped output by default (relative path, then `line: code`), with `mode: "files"`, `pathPrefix`, `excludeTests`, and a top-files summary for popular symbols
- `initialize` returns usage instructions; `GET /health` reports open solutions, indexing and pending disk changes; results note when the index is still building
- With several solutions open, a `filePath` picks the solution that contains it; relative paths may start at the repository root or the solution's folder

## [0.11.2]

- Fix `get_file_errors` failing with an `IndexOutOfRangeException` from ReSharper's injected-language scan: the tool no longer asks string literals for references (an injected regex or HTML is never a compile error), so the scan is not triggered
- `get_file_errors` now isolates failures per node and per file: a node ReSharper cannot inspect is reported as a warning and a file that fails no longer loses the rest of the batch

## [0.11.1]

- Keep Rider navigation responsive during MCP searches and usages by running read operations as interruptible background work
- Prevent daemon analysis under a write lock in `apply_suggestions` and `apply_quick_fix`, addressing the reported navigation deadlock
- Revalidate prepared fixes when IDE writes intervene before application; keep suggestion listing and dry runs read-only
- Use indexed lookup for absolute and solution-relative file paths

## [0.11.0]

- Fix `rename_symbol` failing with a NullReferenceException on newer Rider builds (e.g. 2026.2): the rename machinery reads the ambient `CompilationContextCookie`, which the IDE establishes but a headless caller must set up itself
- Fix `list_quick_fixes` and `apply_quick_fix` on newer Rider builds: expand quick-fixes via the stable `IQuickFix.CreateBulbItems()` instead of an internal API whose signature differs across builds
- `apply_quick_fix` now accepts the `fixId` that `list_quick_fixes` reports (e.g. `OptimizeImportsFix`) as well as the fix's display text — previously the two tools disagreed on what a fix id was
- Better bug reports: tool errors now include the full exception with stack trace, and exceptions crossing from the R# thread keep their original stack

## [0.10.0]

- New `apply_suggestions` tool: apply inspection quick-fixes across a whole file by inspection id — e.g. convert every explicit constructor into a primary constructor. Position-free and file-wide (complements `apply_quick_fix`), driving ReSharper's "Fix all in file" engine headlessly. Filter with `inspectionIds` or pass `all`; preview with `dryRun`; batch via `filePaths`

## [0.9.0]

- Nine new agentic tools for navigation, diagnostics, and refactoring
- Read/navigation: `get_symbol_source` (full declaration source), `get_call_hierarchy` (incoming/outgoing callers), `get_type_hierarchy` (super/subtype tree)
- Diagnostics: `get_diagnostics` (daemon inspections with severity and quick-fix availability), `list_quick_fixes` (available bulb actions at a position), `complete_at` (code completion suggestions)
- Write/refactoring: `rename_symbol` (semantic solution-wide rename with dry-run support), `generate_members` (generate constructors/overrides/etc.), `apply_quick_fix` (apply a bulb action at a position)

## [0.8.0]

- New `flow` tool: describe the control flow of a method or type — execution steps, branch conditions, loops, try/catch paths, inlined call targets (configurable depth), and why-hints from code comments

## [0.7.0]

- Batch mode: most tools now accept multiple inputs in a single call, reducing round-trips when querying several symbols or files at once
- Symbol-based tools (`find_usages`, `get_symbol_info`, `find_implementations`, `go_to_definition`) accept a `symbols` array
- File-based tools (`get_file_errors`, `list_symbols_in_file`, `fix_usings`, `format_file`) accept a `filePaths` array
- `search_symbol` accepts a `queries` array; `browse_namespace` accepts a `namespaceNames` array
- `find_usages` now supports `maxResults` parameter to cap results on widely-used symbols

## [0.6.1]

- Fix deprecated `URL(String)` constructor usage — replaced with `URI(String).toURL()` for Java 20+ compatibility

## [0.6.0]

- New **format_file** tool: format, clean up, or apply code style to files using ReSharper's CodeCleanupRunner
- Peer-to-primary promotion: when the primary Rider instance closes, a peer automatically takes over the primary port
- Watchdog health checks: periodic self-checks detect unresponsive servers and trigger recovery
- find_usages now transitively searches interface/base method usages — finds call sites through abstractions
- find_implementations supports `maxResults` parameter to prevent overflow on widely-implemented interfaces
- Smarter file resolution: relative paths, case-insensitive matching, and suffix/partial path matching
- Better error messages: unresolvable symbols show the reference name; wrong-kind symbols suggest the correct kind
- HttpListener robustness: consecutive errors trigger in-place listener recovery; restart waits for old thread to exit
- Source-generated and sourceless types handled gracefully instead of crashing
- Symbol resolution reordered: references resolved before declarations for correct cursor-on-usage behavior

## [0.5.1]

- Streamable HTTP transport: upgrade MCP protocol from 2024-11-05 to 2025-03-26, enabling compatibility with clients like Codex that use the newer transport
- GET requests now return a valid SSE stream (text/event-stream) instead of 405, so clients that probe with GET during discovery work correctly
- DELETE requests accepted for session termination per Streamable HTTP spec
- Mcp-Session-Id header sent on all responses; mismatched session IDs rejected with 404 per spec
- JSON-RPC notifications (no id) now correctly return 202 Accepted with no body
- Protocol version negotiation: server accepts both 2024-11-05 and 2025-03-26 clients
- Proper Allow header (GET, POST, DELETE, OPTIONS) on 405 responses

## [0.5.0]

- New **fix_usings** tool: automatically adds missing using directives in C# files by resolving unresolved type references against the solution and referenced assemblies
- Unambiguous types (single candidate namespace) are auto-fixed; ambiguous types are reported with candidates
- Interactive disambiguation: call again with `resolutions` parameter to resolve ambiguous imports (e.g. `{"ILogger": "Microsoft.Extensions.Logging"}`)
- First write-capable tool — introduces IMcpWriteTool marker interface and write lock dispatch infrastructure

## [0.4.1]

- Fix symbol-by-name resolution for partially qualified names (e.g. "MyClass.MyMethod") — previously failed when the input wasn't a full namespace path

## [0.4.0]

- Compact text output: all tools now return concise plain-text instead of verbose JSON, significantly reducing token usage for LLM consumers
- New FormatSignature helper produces one-line signatures like "static method DoStuff(x:int) : void"
- Members in get_symbol_info shown as compact signature lines instead of nested JSON objects
- search_symbol, find_usages, find_implementations, list_symbols_in_file, browse_namespace, get_solution_structure, get_file_errors all use compact text format

## [0.3.1]

- Path-segment matching for solutionName: use a unique directory name (e.g. "my-repo") instead of the full path to target a solution
- list_solutions now includes uniquePathSegment hints for solutions with duplicate names
- Increase default snippet length from 200 to 2000 characters for richer context in tool results

## [0.3.0]

- Multi-solution support: multiple Rider instances accessible through a single MCP endpoint using primary/peer architecture
- New list_solutions tool to discover all open solutions
- Optional solutionName parameter on all tools for targeting a specific solution
- Automatic stale peer cleanup when a Rider instance closes or crashes

## [0.2.1]

- Performance: search_symbol and symbol-by-name resolution now use R#'s symbol cache instead of brute-force PSI tree walking — orders of magnitude faster on large solutions
- Fix cascading timeout bug: timed-out requests no longer block the R# thread, preventing snowball failures when parallel tool calls queue up
- Increase tool execution timeout from 30s to 120s to handle queued parallel requests
- Fix macOS plugin install path (plugins/ subdirectory)

## [0.2.0]

- find_usages: results grouped by project and file with counts; excludeDeclarationFile option; per-line deduplication
- find_implementations: direct vs indirect classification with via chain
- get_symbol_info: filter compiler-generated record members (operators, accessors, parameterless ctors)
- search_symbol: dot-qualified queries (e.g. "IProfile.Fake") match by ContainingType.MemberName
- list_symbols_in_file: filter get_/set_/add_/remove_ accessor methods
- browse_namespace: flag generated source files

## [0.1.0]

- Initial release with 9 MCP tools
- Language-agnostic PSI support (C#, F#, VB)
- Symbol resolution by name or file position
- Ambiguity detection with candidate list
