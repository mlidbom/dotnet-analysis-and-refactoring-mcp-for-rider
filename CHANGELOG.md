# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Versions up to 0.12.0 are Joshua Light's MCP Server for Code Intelligence (`com.j-light.resharper-mcp`), which this
plugin is a fork of.

## [Unreleased]

### Added

- `tools/list` now tells MCP clients which tools only read the solution, including code completion, and which can edit files or reload projects. The same annotations reach clients when a peer supplies the tool list.
- The standalone primary, `Royder.Primary`, for Windows: it serves the MCP endpoint on `127.0.0.1:23751` whether or not a desktop Rider runs, and passes a call for a solution open in desktop Rider on to that Rider. An absolute file path, or a full `.sln` or `.slnx` path as `solutionName`, opens the solution in one private Rider that holds every solution the primary opens, in a trusted repository's main checkout or any of its linked worktrees, and the call is served once the solution is indexed and its source generators' output is in. The private Rider keeps its project settings outside the checkout, applies the everyday Rider's global ReSharper settings, and serves the newest tools build deployed, as the desktop Riders do. The settings trust the repositories they list in `repositories`, and every main Git checkout directly in a folder they list in `repositoryFolders`, such as `C:\Dev`; both are read at each call, so a repository cloned into such a folder is trusted from the next one. A call on a checkout of no trusted repository is refused with what is trusted. Building, installing and updating it: `dev_docs/headless-royder/standalone-primary.md`
- The primary answers every tool call within 55 seconds, `answerWithinSeconds`, before Claude Code gives up on it at 60 with only "The operation timed out.": a solution still opening answers how long its warm-up has run and what it waits for, such as Rider indexing it, and a call Rider is still working on answers that it goes on there. Rider gets 4 minutes to open a solution, counted once no other worktree's open or close is in the way, and then 15 to load, index and generate it. A `solutionName` that isn't a solution of its worktree is answered with why, the file missing or not in Git, and with the worktree's solutions
- The primary's `warm_worktree` opens a solution ahead of the first call, and `close_worktree` closes one, answering `Closed` once its backend and the processes it started have exited, so that the folder is free, or else `Still closing`, which a second call waits on; it also closes one whose worktree Git no longer lists. A solution unused for 30 minutes is closed, and the private Rider with the last of them. A solution whose file changes, such as by a project added to it, is reopened whole at the next call. `Royder.Primary remove-worktree` closes and removes a linked worktree, and a worktree that Git removes or replaces is noticed
- The primary installs as a task at Windows sign-in, `tools/Register-RoyderPrimaryAtLogon.ps1`, which runs it in a console with no window and starts it again within a minute of its end; `tools/Update-InstalledRoyderPrimary.ps1` switches to a new package once the calls in flight are answered, keeping the old settings for a rollback. After a Rider update, each on-demand open answers that Rider was updated, until a package built on the new Rider is installed
- With the standalone primary installed, the plugin takes its port from the primary's settings, `desktopPort` in `%LOCALAPPDATA%\DotNetAnalysisAndRefactoringMcp\primary\settings.json`, so that agents and the symbol linker keep the address `127.0.0.1:23751`, which the primary then serves. `DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT` still comes first
- `find_usages`, `go_to_definition`, `get_symbol_source` and `list_symbols_in_file` carry `_meta: {"anthropic/alwaysLoad": true}`, so that Claude Code 2.1.285 and later loads them at a session's start, rather than leaving them for the agent to find through its tool search
- `/health` says of each solution whether Rider has synchronized its source generators' output, `sourceGeneratorsReady`: `null` for a solution with no source generators

### Changed

- `get_symbol_source` answers in text: the declaration's whole lines, numbered as a file read shows them, under a `Name — path:first-last` heading. It answered a JSON object holding the source in an escaped string, without line numbers, its first line cut at the declaration's first column. A declaration over 20,000 characters is cut at a line, and the answer says where to read on
- `get_call_hierarchy`'s outgoing tree answers in text, an indented outline of `Type.Member  path:line` with paths relative to the work tree, and in JSON with `format: "json"`. Besides calls through a name, it follows `new`, properties with code, indexers, user-defined operators and methods passed as delegates, and lists an interface, abstract or virtual member's implementations or overrides under a call to it. It shows the solution's own code, counting by assembly the calls into compiled code it leaves out: `namespaces` narrows it, `assemblies` adds compiled assemblies by name. A callee met again points to its calls listed above instead of repeating them. For `GetSymbolSourceTool.Execute` at depth 2 the answer is 36 lines in 4,615 characters, where it was 48 nodes, 32 of them calls into compiled code, in 16,912 characters of JSON
- `get_call_hierarchy`'s incoming tree answers in the same outline, each caller with the lines on which it calls the member above, as `find_usages` shows them, and says when it calls through an interface or base member, or passes it as a delegate; in JSON with `format: "json"`. It finds the calls the outgoing tree follows: `new`, a constructor's base call, properties, through the accessor that runs, indexers and operators, as well as calls through a name; and a property's callers include the code that reads an auto-property. A caller met again points to its callers listed above, and `namespaces` narrows the callers. It answered JSON with each file's full path at every node and no line of code
- `get_call_hierarchy`'s outgoing tree follows a property set in an object initializer
- `get_type_hierarchy` answers in text, the call trees' indented outline: `abstract class Shapes.Drawing.Renaming.Segment  Shapes.Drawing\Renaming\Segment.cs:4`, each type under the one it inherits from or that inherits from it, a supertype with the type arguments its subtype gives it, `System.IEquatable<Callee>`, and a compiled one with its assembly. A type met again points to the types beyond it listed above. It answered JSON with each type's name twice, its file's full path, and an empty `"children": []` at every leaf
- `get_diagnostics` answers in text, as `inspect_code` does: a heading with the file and how many issues at which severity, then a line for each, `9:9 warning NotAccessedVariable.Compiler: Local variable 'neverRead' is only assigned but its value is never used  (quick-fix)`. It answered JSON with ten fields an issue
- A name not found is answered with suggestions whose paths are relative to the work tree, where they were full paths
- `search_symbol`, `go_to_definition`, `get_symbol_info`, `find_implementations`, `get_solution_structure` and `get_file_errors` write paths relative to the work tree, as `find_usages` does, where they wrote each file's full path. `go_to_definition`'s JSON form keeps the full path. `get_file_errors` writes an error as `  6:17 error: Cannot resolve symbol 'DrawingStencil'`, where it was `error :6:17 — ...` after a blank line, and `1 diagnostic`
- `go_to_definition` shows a declaration's doc comment by its summary, `doc: ...`, as `get_symbol_info` does, an `<inheritdoc/>` replaced by what it inherits, and then the declaration's whole lines from after that comment, numbered as `get_symbol_source` numbers them, up to `maxTextLength` characters and the line that says where it goes on. By default it showed the declaration's first 2,000 characters, where its description said 200, cut in the middle of a line and starting with the doc comment's `///` and tags. A compiled member gets its documentation's summary too
- `find_implementations` lists each implementation on a line, `[direct] sealed class Shapes.Circle — Shapes\Circle.cs:4`, by its qualified name and with its modifiers, and under it the summary of its own doc comment, not one it inherits. It named an implementing member by its name alone, `method Execute`, so that only the path told which type it was in, and showed the first 200 characters of each one's source, doc comment first. When `maxResults` stops it, it says `50 implementations, and more past maxResults`, where it said `showing 50 of 50+`
- `rename_symbol` answers in text: a line saying what it renamed, or would rename in a dry run, or refused to, `Renamed method Shapes.IShape.Area to Surface.`; each conflict on a line, `error conflict: Containing type has the same name as the new name`; then the files it changed by their paths in the work tree, a file it renamed by its new path, `Shapes\Disk.cs, renamed from Circle.cs`. A dry run with an error conflict says that the rename would be refused. It answered JSON with each file's full path and then its name again: 6,098 characters for a rename that changes 28 files, now 2,151
- `rename_symbol` refuses a rename for which ReSharper reports an error conflict, such as "Usage of ... cannot be updated correctly", and answers with the conflicts and the files it would change; `applyDespiteConflicts: true` applies it anyway, and warnings don't stop it. It applied the rename, as when a `cref` lost its type qualifier and the build warned CS1574
- `rename_symbol`'s description says what a rename follows, as Rider's Rename does: a member's implementations and overrides, the `<see cref>`, `<paramref>` and `<typeparamref>` tags that name the symbol, a positional record's property with its parameter, and a type's file. It mentioned only the file
- The guide a client gets on connecting is one sentence on which tools to use instead of a text search: `go_to_definition`, `get_symbol_source` and `find_usages` to look up C# symbols, and `list_symbols_in_file` before reading a big file, within the 250 characters Codex shows before it discovers the tools. The primary's adds that an absolute path opens its checkout. It was a heading that said only whose code intelligence it is, and ten usage notes

### Fixed

- Under heavy load, the first call after a file was deleted or added on disk could answer as if it hadn't been, after waiting 10 seconds, with a note that the file was still, or not yet, in Rider's project model: Rider's frontend had taken the request to rescan the file's folder and then not passed the change on. While a call waits for Rider, it now asks again each second
- `find_usages` lists the usages in code a source generator wrote, under the generated file, `[generated by Shapes.Generators.SwatchGenerator] Swatch.g.cs`, and counts them: it left them out
- The text answers name where a source generator's code is by its generated file and the generator, `[generated by Shapes.Generators.SwatchGenerator] Swatch.g.cs:6`, wherever they write a path for any other file: a declaration in `go_to_definition`, `get_symbol_info`, `get_symbol_source`, `find_usages` and the call trees, and a call in the incoming tree. They said `[no source]` or `[generated]`, or put a member generated into a partial type at the type's other part; `get_symbol_info` said nothing of where it is declared, and the incoming tree wrote such a call's line as `:6:`
- `get_diagnostics` and `inspect_code` say, beside a cref to a C# 14 extension block's member, `extension(T).Member`, that ReSharper 2026.3 resolves no such cref and only a build that generates the documentation file checks it. They gave its "Cannot resolve symbol 'extension'" as one more cref to fix, of a comment the compiler accepts
- `rename_symbol` lists only the files whose text it changed. A rename right after a dry run listed every file the dry run had changed as well: ReSharper's symbol cache binds those files' declarations anew in the next transaction, which counted them
- A member's name stands for every overload of it in `find_usages`, `go_to_definition`, `get_symbol_info`, `find_implementations` and `get_call_hierarchy`, and in `get_symbol_source` with `allDeclarations`; without it, `get_symbol_source` says how many overloads there are. Each worked on whichever overload ReSharper listed first. A position still stands for the one there. `go_to_definition`'s JSON form gives a declaration for each overload, each with a `signature`
- A signature, in every tool's text and in the JSON forms' `signature`, writes a parameter's modifiers before its name, `this`, `params`, `ref`, `out`, `in` or `ref readonly`, and its default value after its type, `unit:string = "cm"`: what tells apart overloads such as `TryParse(s:string, out result:int)`. A compiled member's types are written as C# writes them, `int` and `char[]`, where they were `Int32`, and an array's type was left empty
- `get_call_hierarchy` tells overloads apart, and follows calls from one overload into another; its outgoing tree follows the calls an expression-bodied member makes, which it missed
- A type declared in parts is shown by the part in the file named after it, `IThreadGate.cs` rather than `IThreadGate.Implementation.cs`, wherever a tool shows one declaration
- A position past the end of its line answers that the column is outside the line, quoting the line as Rider has it, where it answered that ReSharper failed with an `ArgumentOutOfRangeException`
- `list_symbols_in_file` keeps a type's members under it after a generic method, where they came under a heading named after the type parameter
- `get_call_hierarchy`'s incoming tree no longer takes a doc comment's `cref` or a `nameof` for a call, nor lists an override as calling itself through its own `base` call
- An edit right after a file changed on disk, such as a `rename_symbol` in a file a script had just rewritten, lands where it should: before an edit, the disk sync gives the file watcher 100 ms and asks Rider's frontend to rescan every file it suspects. When one of Rider's two copies of the file hadn't yet taken in the change, the edit's changes landed at the old offsets, turning `KindOfUnansweredItem` into `KinSuggestionsm`. A saved file that doesn't hold the text the edit gave it gets a WARNING in the answer, with the first line that differs
- After an edit, a file whose copy in Rider went back to the text before the edit, as when Rider reads in its own save before the file is written, is reconciled with disk. The edit answered, after a 10-second wait, that Rider hadn't saved the file, and `move_type_to_namespace` left the file where it was
- `get_file_errors` and `inspect_code` with `changedSince` make one git call fewer, the one that timed out on a machine under heavy load, and a git call that takes longer than 15 seconds answers to retry, or to name the folders to cover in `filePaths`. It answered "git rev-parse timed out after 15s"
- A call naming a file in a Git worktree no open solution is in now receives an immediate routing error unless it explicitly selects a solution. Previously the caller could wait 10 seconds for a reply that the file was in no project, with `isError` set to `false`.
- A peer Rider that ended without deregistering, such as in a crash, is now forgotten before the next call is routed, once nothing listens on its port, and otherwise at the first call to it, which answers that the solution is no longer available: `list_solutions` drops every solution registered on its port, and a call that names no solution is no longer refused with "Multiple solutions are open". Until now each call to it failed, and `list_solutions` went on listing it.
- The first call after a file is deleted on disk now sees the deletion: `search_symbol` and `find_usages` no longer find the types it declared, and `get_file_errors` reports their uses as unresolved. Until now Rider could still have the file for some seconds. A file that Rider keeps in its project after deletion, such as a linked one, delays only the first call after it, which says so.

## [0.15.1]

### Fixed

- A rename right after a dry run of it could leave doc comments stale: renaming a type parameter of Compze's `TeventApplyingPart` renamed every use in code, and none of its `<typeparamref>` tags. A dry run's rollback left the file's tree without its doc comments' references; ReSharper now parses the files a dry run changed anew, for `rename_symbol` and `move_type_to_namespace`
- `rename_symbol` failed with "OperationCanceledException: The operation was canceled.", dry runs included, on a type in a project that references ASP.NET Core, whenever ReSharper hadn't yet cached the project's controllers: Rename looks for them, to find the files to rename with the type, with a search on several threads, which can't read while the tool holds the write lock. It now runs as Rider's Rename does, under a read lock, and so does `move_type_to_namespace`

## [0.15.0]

### Added

- A JSON form of `go_to_definition` and `browse_namespace`, for programs to read, such as the .NET Terminal Symbol Linker: `format: "json"` answers one JSON object, with 1-based lines and columns, bare C# names, and kinds from a fixed set of C# words. `go_to_definition` gives every declaration a name stands for, one shape whether found, ambiguous or unknown; `browse_namespace` gives each namespace's types, the types nested in them, and with `includeMembers`, their declared members, extension blocks with their receivers among them. A solution still loading answers an error rather than a partial result
- `browse_namespace` takes `includeMembers`, in the text form too: each type's declared members under it
- `inspect_code` takes `changedSince` (a git ref) or `changed: true`, as `get_file_errors` does: it inspects every source file changed since then, committed or not, and the untracked ones. The guide a client gets on connecting says which tools take it: it read as `get_diagnostics`' before

### Fixed

- Rider 2026.3 started a second MCP server for each Rider, with no solution, on a port of its own: in the lightweight backend it runs beside the solution's. Whenever that one got the base port first, it served as the primary. The plugin now loads only where ReSharper's inspections run
- `get_symbol_info` with `includeMembers` lists a C# 14 extension block as C# declares it, `extension(circle:Circle)`, with its members under it. It listed the block as a class named `$extension`, and each member as the static method the compiler makes of it, such as a property `Diameter` as `get_Diameter(circle:Circle)`. A member's containing type is the block in its class
- A C# 14 extension block's member is named as C# names it, `ShapeExtensions.Grown`, wherever a qualified name is given, and naming it finds it. Its name was ambiguous with the static method it compiles to, and showed ReSharper's `$extension` (`search_symbol` printed `$extension.Grown`)
- A call that reaches a new file's types by name, such as `search_symbol`, finds them right after the file is created on disk in a project's folder. The call waited for a new file only when it named the file, and found nothing until Rider had taken it in
- A delegate's members are no longer listed as declared: its constructor, `Invoke`, `BeginInvoke` and `EndInvoke` are the compiler's
- `get_symbol_info`'s doc lines keep the names a doc comment links to: every `<see cref>`, `<paramref>` and `<typeparamref>` was left out, so "The sum of the areas of `<paramref name="shapes"/>`." read "The sum of the areas of ."

## [0.14.1]

### Fixed

- Rider logged an error at every start, "Unknown element: getting-started": the plugin's descriptor had an element Rider doesn't know. What it held, how to configure an MCP client, is in the README

### Changed

- The plugin's description on the Marketplace lists every tool: `move_type_to_namespace`, `inspect_code` and `reload_projects` were missing

## [0.14.0]

### Changed

- Requires Rider 2026.3 or later: the plugin is built against its SDK, so it no longer bridges API changes between Rider versions by reflection, and every Rider it supports is the one it is tested on. Rider 2025.3 to 2026.2 keep version 0.13.0
- `rename_symbol` renames a type's file named after it, as Rider's Rename does, and lists it in `renamedFiles`; the file kept the old name. A dry run renames no file
- Edit tools save the files they changed before they answer, so that a build started right after the answer builds the edit: Rider saved them a while later. Files with unsaved changes before the edit, such as someone's typing in Rider, are left for Rider to save. A file Rider hasn't saved within 10 seconds is named in a note, which says how the file differs from Rider's copy
- No use of deprecated platform API: the Kotlin compiler no longer generates a forwarding method for the deprecated `StatusBarWidget.getPresentation(PlatformType)` into the status-bar widget, which the Marketplace's verifier reported as two usages of deprecated API

### Added

- `move_type_to_namespace`: ReSharper's Move to Another Namespace, checked: if the declaration didn't move, nothing is changed and the answer says so. Files that call the type's extension methods get the `using` the move gives them no other way. The file moves to the folder of the new namespace, made if missing, once it holds only types of that namespace, and its namespace is made file-scoped again when the code style has it so. A dry run says where the file would go
- `apply_suggestions` takes a `scope`: `file` (the default), or the file's `folder`, `project` or the whole `solution`, as Rider's Fix in folder/project/solution. The file is where the fix is found
- `apply_suggestions` says how many files each fix changed. A fix in a wider scope skips the files in which Rider's solution-wide analysis found no such issue, and those results lag a change of settings, such as a new `.editorconfig`: with no file changed, the answer says so and why
- `inspect_code`: ReSharper's inspections over files, folders, projects or the whole solution, run by the engine of Rider's Inspect Code against its live model, in parallel. With Rider's solution-wide analysis on and complete, its inspections are in too, such as `UnusedMember.Global`: the whole-solution sweep that took `jb inspectcode`. Counts per inspection, then rg-style lines; filters by severity and inspection id
- `reload_projects`: Rider's Reload Project for the projects of the given files, project files or folders, and a wait until Rider has loaded them and ReSharper has indexed the result. For a project whose model in Rider is stale, such as a new view's code-behind that reports `InitializeComponent` unresolved
- The server tells connected clients when its tools change, with `notifications/tools/list_changed` on the event stream they hold open, so a new tool appears without a reconnect

### Fixed

- `rename_symbol` renames a positional record's property, through the record's parameter that declares it, as Rider's Rename does; it answered that the property "has no source file"
- `rename_symbol`'s `changedFiles` left out the files of the implementations and overrides it renamed with the symbol: it is now the files the rename changed
- Answers ended with `(index still building — results may be incomplete)` though the solution was indexed: every edit tool's, as the edit's own changes, and a dry run's rolling them back, set ReSharper indexing the files they changed; and most answers right after a file was edited on disk, as reloading it does too, for tens of milliseconds. The note now says that ReSharper was indexing as the call's work began, for longer than the second the call waits for it
- Failures are reported, not hidden. `get_diagnostics`, `list_quick_fixes`, `apply_quick_fix` and `apply_suggestions` answered an empty or partial list when ReSharper failed to read a highlighting or a fix, or a daemon stage failed; they now say what was skipped, and when the list may be incomplete. `apply_quick_fix`, `complete_at` and `generate_members` answered a failure as an ordinary result; it is now an error, logged
- An edit tool went ahead on a file it couldn't read from disk, such as one another process was writing, as if Rider's copy matched it; such a file now counts as differing, and edits wait for it
- `/health` read as ready when a solution's own health check failed; such a solution now counts as not ready, and says why
- The primary server dropped a peer Rider whose call was slow or answered an HTTP error; it now drops one only when nothing listens on its port
- A line or column outside the file is answered with what's out of range, by every tool that takes a position
- `search_symbol` finds a type by its qualified name, `Shapes.Circle` or `System.IO.MemoryStream`, and a member through a qualified type, `Shapes.IShape.Area`; a qualified name was taken for `Type.Member` and found nothing
- `get_symbol_info`'s member listing says each member's access, `private`, `public` and so on: a compiled type's listing holds its private members too, and nothing told them apart
- `get_call_hierarchy` on an implementation finds the callers that call it through its interface or base member, and says which member each was found through; it found none before
- `get_call_hierarchy` finds callers that are properties, fields or constructors, such as an expression-bodied property; only method callers were found before
- `find_usages` follows the source members a member implements or overrides, through ReSharper's own super-member search instead of a match by name and parameter count, and no longer follows compiled ones: following `IDisposable.Dispose` listed every `using` in the solution. It names the compiled ones it didn't follow
- A position on the variable of a `using var` line resolves to the variable, not to the `Dispose` the `using` calls: `rename_symbol` there renamed the type's `Dispose` method. Generally, only a reference whose own text holds the position counts
- Declaration locations (`get_symbol_info`'s `declared:`, `go_to_definition`, `search_symbol`, `find_usages`, `find_implementations`, `get_type_hierarchy`, `browse_namespace`, `generate_members`) are the line and column of the declaration's name, not the first line of its doc comment

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
