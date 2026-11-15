# .NET Analysis&Refactoring MCP — Project Guide

## What This Is

A fork of Joshua Light's `resharper-mcp`, renamed on 2026-11-14 (see `dev_docs/swappable-tools.md`).
An MCP (Model Context Protocol) server that runs **inside** the ReSharper/Rider backend process,
exposing code intelligence features to AI assistants via HTTP.

## Architecture

### Two-part Rider plugin

Rider plugins require both a JVM frontend and a .NET backend:

- `rider-plugin/META-INF/plugin.xml` — minimal IntelliJ plugin descriptor (no Java/Kotlin code needed for backend-only plugins). Without this JAR, Rider silently ignores the `dotnet/` folder.
- `src/DotNetAnalysisAndRefactoringMcp/` — the .NET backend that runs inside the ReSharper host process: **the host**.
- `src/DotNetAnalysisAndRefactoringMcp.Tools/` — **the tools**, which the host loads into a collectible load context
  and swaps for a newer build between calls, so tool changes need no Rider restart. Read `dev_docs/swappable-tools.md`
  before changing either: code in the tools follows rules so that a swapped-out build can be freed, and the
  specifications check that it was.

### Installed plugin layout (in Rider's plugins folder, such as `%APPDATA%\JetBrains\Rider2026.3\plugins\DotNetAnalysisAndRefactoringMcp\`)

```
lib/DotNetAnalysisAndRefactoringMcp.jar       # plugin.xml descriptor (tells Rider this plugin exists)
dotnet/DotNetAnalysisAndRefactoringMcp.dll    # the host (loaded into ReSharper host)
tools/<build>/                                # the tools build it was installed with, and host.sha256
```

### MCP transport

The official C# MCP SDK (`ModelContextProtocol` NuGet) requires .NET 8+ for HTTP transport,
but ReSharper plugins target **net472**. So we implement a lightweight MCP server manually:

- `McpHttpServer.cs` (host) — `HttpListener`-based HTTP server on `127.0.0.1:23751`; hands every request to the tools build
- `Serving/McpRequest.cs` (tools) — implements JSON-RPC 2.0 directly (`initialize`, `tools/list`, `tools/call`, the `internal/*` methods)
- Protocol types in the tools' `Protocol/JsonRpc.cs` and `Protocol/McpTypes.cs`, written with `JsonOf`, never Newtonsoft's serializer
- Uses `Newtonsoft.Json` (bundled with Rider's ReSharper host, no need to ship it)

### Multi-solution support (primary/peer architecture)

Rider opens each solution in a separate OS process, so multiple instances compete for ports.
The plugin uses a **primary/peer** model:

- **Primary** — the first Rider instance binds the base port (23751). It acts as the single MCP endpoint for all clients.
- **Peer** — subsequent instances bind the next available port (23752, 23753, ...) and register themselves with the primary via `internal/register` / `internal/deregister` JSON-RPC methods.
- The primary proxies `tools/call` requests for peer solutions by forwarding HTTP requests to the peer's port. If a proxy fails (peer crashed), the stale registration is automatically removed.

Component breakdown:

- `McpShellComponent.cs` — `[ShellComponent]` (process-level singleton). Tries to bind the base port; on failure, increments and becomes a peer. Owns the `McpHttpServer`; its watchdog lets a peer take over the primary's port.
- `McpServerComponent.cs` — `[SolutionComponent]` (per-solution). Registers/unregisters the solution with the server on solution open/close, and holds what of it outlives a tools build. Skips solutions without a file (Rider 2026.3's lightweight backend has one).
- In the tools, `Serving/`: `OpenSolutions` routes a call (`LocalSolution`, `PeerSolution`), `PrimaryServer` is a peer's registering with the primary, `OtherMcpServer` reaches another Rider's server.
- When only one solution is open (across all Rider instances), all tool calls route to it automatically (backwards-compatible).
- When multiple solutions are open, callers must specify `solutionName` in tool arguments.
- The `list_solutions` meta-tool returns all currently open solutions (local + peers).
- `solutionName` matches case-insensitively against the solution filename (without `.sln`) or the full path, or else a
  unique segment of its path, which `list_solutions` gives each of several solutions of one name.
- A call that names files and no solution goes to the solution holding them most closely: its own folder before the
  work tree around it, so two solutions of one repository each get their own files.
- The specifications in `Serving/WithAPeer` run a second sandbox Rider as the first one's peer (`peer start|stop|status`
  in the development tools).

### ReSharper integration

- `PsiHelpers.cs` — shared helpers: file lookup, position-to-node resolution, element resolution, snippet truncation.
- `Tools/IMcpTool.cs` — common interface for all tools. Each tool provides its name, description, JSON schema, and execute method.
- `Tools/IMcpWriteTool.cs` — marker interface for tools that modify the PSI tree (requires write lock + PsiTransaction).
- `Tools/IMcpEditTool.cs` — `IMcpEditTool` marks tools that edit source files (refused while a file is stale; dry runs pass). `IMcpExpandableTool` lets a tool widen its arguments before any lock (e.g. `changedSince` → `filePaths` via git).
- `Tools/ToolBatch.cs` — the one batch runner: `ToolBatch.Of(arguments, "filePaths", item: "filePath")?.Run(ExecuteSingle) ?? ExecuteSingle(arguments)`. Top-level arguments apply to every item; each item is isolated.
- `SkippedElements.cs` — guard for solution-/file-wide walks: `skipped.Guard(element, () => ...)`, then append `skipped.Line`. Use it in every loop over types or members.

### Disk sync (`Disk/`)

Agents edit files on disk while Rider is in the background, and Rider reloads them late (0.1 s to 9 s,
measured). Before every call `McpServerComponent` runs `DiskSync.Run`:

1. Suspects = files the call names (`FileArguments`) + files `DiskChanges` (a `FileSystemWatcher` over the
   work tree and project directories) saw change + files found stale earlier.
2. Under a read lock, compare each suspect's document text with disk (line endings and BOM normalized).
3. Stale documents without unsaved edits: `DocumentOperationsImpl.SynchronizeDocumentContentsWithStorage` on the
   main thread under the write lock (Rider's own reconcile: an external change, file marked reconciled).
4. Ask the frontend to rescan stale files, new files and their folders: `FileSystemModel.RefreshPaths`
   (`Disk/Rescans.cs`, see gotchas), on the main thread. It is queued and debounced, not synchronous.
5. Poll until the documents match disk (10 s). Closed files match at once after step 3; a tab open in an editor
   matches whenever the frontend reloads it. A file still stale after that wait is "settled": it is not waited
   on again until its disk time changes.

Edit tools are refused while any suspect differs. Read tools get a note instead.

After an edit tool's work, and before its answer, the documents it changed are saved (`Disk/UnsavedDocuments.cs`):
Rider saves what a refactoring changed by itself, right after its transaction, so the call first waits half a second for
that, and then asks the frontend to save what is left (`SaveDocument`), waiting until disk holds their text. A second
save right after Rider's own once left Rider with an empty copy of the file. Documents that differed from disk before
the edit are left alone: someone's unsaved typing.

A tool that moves files (`IMcpFileMovingTool`, such as `move_type_to_namespace`) moves them on disk after that save
(`Disk/FileMoves.cs`), as `git mv` would, and waits until Rider's project model has them where they went. Not through
the project model: a file it moved into a folder new to an SDK-style project became a `<Link>` in the project file.

**Never change a document's text from the backend** (`DocumentBindingUtil.SyncDocumentsWithFiles`, `ChangeDocument`):
it is an in-memory edit, the document counts as unsaved, and Rider's autosave later writes it over disk. Observed:
it reverted a file the agent had just restored. `tests/live_disk_sync.py` checks for this.

## Tools

| Tool | Description |
|------|-------------|
| `find_usages` | Find all references to a symbol (uses `IFinder.FindReferences()`) |
| `get_symbol_info` | Get detailed symbol info: kind, type, params, docs, base types, declaration location |
| `find_implementations` | Find implementations of interfaces/abstract classes and overrides of virtual members |
| `get_file_errors` | Compile errors and unresolved references from the PSI tree; deduped, capped, `changedSince` git mode |
| `search_symbol` | Search symbols by name (substring match) across the solution |
| `go_to_definition` | Navigate from a usage site or symbol name to the declaration location |
| `get_solution_structure` | List all projects, target frameworks, and project-to-project references |
| `browse_namespace` | Browse namespace hierarchy: child namespaces and types in a namespace |
| `list_symbols_in_file` | List all declarations in a file (types, methods, properties, etc.) |
| `list_solutions` | List all currently open solutions (server-level meta-tool) |
| `fix_usings` | Fix missing C# using directives by resolving unresolved type references against the symbol cache |
| `flow` | Describe control flow of a method or type: execution steps, branches, loops, error paths, inlined call targets, why-hints |
| `get_symbol_source` | Get the full declaration source code of a symbol (not just a truncated snippet) |
| `get_call_hierarchy` | Build a call hierarchy tree for a method: `incoming` (callers via `IFinder.FindReferences`) or `outgoing` (callees) |
| `get_type_hierarchy` | Get the inheritance hierarchy of a type: `supertypes` (base/interfaces) or `subtypes` (`FindInheritors`) |
| `get_diagnostics` | Run the daemon (`DaemonHighlightingCollector`) on a file: severity, inspection id, message, location, quick-fix availability |
| `inspect_code` | Inspect Code's engine (`CollectInspectionResults`) over files, folders, projects or the solution; solution-wide inspections when SWEA is complete |
| `list_quick_fixes` | List ReSharper quick-fixes (bulb actions) available at a position |
| `complete_at` | Get code completion suggestions at a caret position (runs on the R# main thread) |
| `reload_projects` | Rider's Reload Project for the projects of the given files, waited for until loaded and indexed |

Mutation-capable tools (main-thread execution, with dispatch controlled by the marker):

| Tool | Marker | Description |
|------|--------|-------------|
| `rename_symbol` | `IMcpSelfTransactingWriteTool` | Semantic solution-wide rename via `RenameRefactoring`; manages its own transaction to support `dryRun` rollback |
| `move_type_to_namespace` | `IMcpSelfTransactingWriteTool` | `MoveToNamespaceWorkflow` headless; checks the declaration moved, adds usings for extension-method callers, moves the file to its namespace's folder |
| `generate_members` | `IMcpWriteTool` | Generate members via `GeneratorWorkflowFactory`; relies on the framework's auto-commit transaction |
| `apply_quick_fix` | `IMcpAsyncTool` | Apply a bulb action via `BulbActionExecutor` (which self-transacts) |
| `apply_suggestions` | `IMcpAsyncTool` | Apply scoped quick-fixes by inspection id via `IModernManualScopedAction.ExecuteAction` (ReSharper's "Fix all in file/folder/project/solution" engine, `scope`); complements position-based `apply_quick_fix` |

> `complete_at` is logically read-only but implements `IMcpSelfTransactingWriteTool` solely to obtain main-thread dispatch (the completion engine asserts the R# main thread); it performs no writes.

### Symbol resolution

All tools that take a symbol accept **two modes**:
- `filePath` + `line` + `column` (1-based) — resolve from a specific code position
- `symbolName` — resolve by name (e.g. `"MyClass"`, `"Namespace.MyClass"`, `"MyClass.MyMethod"`)
- Optional `kind` filter (`"type"`, `"method"`, `"property"`, `"field"`, `"event"`) to disambiguate

When multiple symbols match a name, tools return an **ambiguity error** listing all candidates with their qualified names, kinds, and locations — so the caller can pick the right one.

`search_symbol`, `get_file_errors`, `get_solution_structure`, `browse_namespace`, and `list_symbols_in_file` use their own input formats.

`search_symbol` excludes namespace declarations by default to reduce noise. Pass `includeNamespaces: true` or add `"namespace"` to the `kinds` filter to include them.

### Batch mode

Most tools support batch mode — processing multiple inputs in a single tool call:

- **Symbol-based tools** (`find_usages`, `get_symbol_info`, `find_implementations`, `go_to_definition`) accept a `symbols` array of objects, each with `{symbolName, kind, filePath, line, column}`.
- **File-based tools** (`get_file_errors`, `list_symbols_in_file`, `fix_usings`, `format_file`) accept a `filePaths` array of strings.
- **`search_symbol`** accepts a `queries` array of strings.
- **`browse_namespace`** accepts a `namespaceNames` array of strings.

Results are concatenated with `=== [N/total] label ===` separators. Shared options (e.g. `maxResults`, `kinds`, `mode`) are specified at the top level and apply to all items. Single-input parameters remain for backward compatibility.

### Language support

PSI file resolution is **language-agnostic** (`GetPsiFiles<KnownLanguage>()`), supporting C#, F#, VB, and any language with a ReSharper PSI implementation.

## Key Findings & Gotchas

### ReSharper SDK
- `JetBrains.ReSharper.SDK` 2026.3.0, the SDK of the Rider Magnus runs, targeting `net472`: the SDK provides reference
  assemblies, and builds on Linux too (CI).
- Its packages pin `System.Threading.Tasks.Extensions` at exactly 4.5.4 while some of its own assemblies were built
  against 4.6.0. Both projects suppress the MSB3277 this gives: it is inside the SDK's graph, at compile time only.

### SolutionComponent attribute
The parameterless `[SolutionComponent]` constructor is **obsolete** since 2025.3. Must use:
```csharp
[SolutionComponent(Instantiation.ContainerAsyncAnyThreadSafe)]
```
The `Instantiation` enum lives in `JetBrains.Application.Parts`.

### Threading model
PSI reads run on ReSharper's JetPool through `McpToolExecution.Read`, using
`StartConstrainedReadActionAsync` and `IPsiFiles.AllDocumentsAreCommittedConstraint` (see below for why not
`AllDocumentsAreCommittedReadConstraint`).
Do not use ordinary .NET thread-pool tasks for PSI access. Read callbacks may be
interrupted and retried when IDE writes arrive; keep them free of side effects.

`IMcpAsyncTool` coordinates analysis and mutation. `ReadThenMain` prepares under a
background read lock and uses the SDK's main-thread continuation, which reruns
preparation if a write intervenes. `apply_suggestions` and `apply_quick_fix` use
this path; daemon analysis must never run under a write lock. Their scoped/bulb
actions own their write transactions. Listing and dry-run paths stay read-only.

Other `IMcpWriteTool` implementations retain main-thread write dispatch. Each
request has a solution-bound lifetime and a 120-second timeout; termination
cancels queued work and requests interruption of active background reads.

### Getting PSI files
`IPsiSourceFile.GetPrimaryPsiFile()` does not exist. For language-agnostic access:
```csharp
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Files;
sourceFile.GetPsiFiles<KnownLanguage>(PsiLanguageCategories.Primary); // the file's own tree, any language
```
Always pass `PsiLanguageCategories.Primary`. Without it, `GetPsiFiles` also builds injected-language files, which
runs ReSharper's injection scan over every string literal: slow, and it crashed with an NRE in `GetRuntimeFeatures`
on TPS files (the root cause of the old `go_to_definition` / `get_file_errors` crashes near string literals).
For a specific language: `sourceFile.GetDominantPsiFile<CSharpLanguage>()` (requires `T : PsiLanguageType`).

### Document coordinates
`DocumentCoords` takes typed intrinsics, not plain ints:
```csharp
var docLine = (Int32<DocLine>)(line - 1);    // 0-based
var docColumn = (Int32<DocColumn>)(column - 1);
var coords = new DocumentCoords(docLine, docColumn);
```

### Document range from tree nodes
`GetDocumentRange()` is an extension in `JetBrains.ReSharper.Psi.Tree.TreeNodeExtensions`. If overload resolution fails, call it explicitly:
```csharp
var range = TreeNodeExtensions.GetDocumentRange(node);
```

### Document offset to coordinates
`IDocument.GetCoordsByOffset()` is obsolete. Use:
```csharp
var coords = documentOffset.ToDocumentCoords();
```

### Type presentation
`PresentationLanguageForTests` does not exist. Use `CSharpLanguage.Instance` as fallback:
```csharp
var lang = declaredElement.PresentationLanguage ?? CSharpLanguage.Instance;
type.GetPresentableName(lang);
```

### Abstract check on types
`ITypeElement.IsAbstract()` doesn't exist. Cast to `IModifiersOwner`:
```csharp
if (typeElement is IModifiersOwner m) result["isAbstract"] = m.IsAbstract;
```

### Namespace access
`GetContainingNamespace()` is on `ITypeElement`, not `IClrDeclaredElement`. For non-type elements, go via `GetContainingType().GetContainingNamespace()`.

### Symbol scope for namespace browsing
`LibrarySymbolScope` is in `JetBrains.ReSharper.Psi.Caches`:
```csharp
using JetBrains.ReSharper.Psi.Caches;
var symbolScope = psiServices.Symbols.GetSymbolScope(LibrarySymbolScope.NONE, caseSensitive: true);
var globalNs = symbolScope.GlobalNamespace;
globalNs.GetNestedNamespaces(symbolScope);
globalNs.GetNestedTypeElements(symbolScope);
```

### Project references
`GetProjectReferences` requires a `TargetFrameworkId`:
```csharp
using JetBrains.ProjectModel.Impl;
foreach (var tfm in project.TargetFrameworkIds)
    foreach (var reference in project.GetProjectReferences(tfm))
        var name = ProjectReferenceExtension.GetReferencedName(reference);
```
`IProjectToProjectReference` does **not** have `ResolveReferencedProject()` — use `GetReferencedName()`.

### Headless refactorings need a CompilationContextCookie
ReSharper's refactoring machinery reads the ambient `CompilationContextCookie` (e.g. the naming
suggestions built inside the `RenameDataModel` constructor resolve runtime features through it).
The IDE's actions establish that cookie before invoking a workflow; a headless caller must do it
itself, otherwise the machinery falls back to the universal context and NREs deep inside
`ModuleReferenceResolveContextExtensions.GetRuntimeFeatures` (observed on Rider 2026.2):
```csharp
var resolveContext = declaredElement.GetSourceFiles().FirstOrDefault()?.ResolveContext;
using var cookie = CompilationContextCookie.GetOrCreate(resolveContext);
```

### Asking the frontend to rescan: `RdFsRefreshRequest`
It takes a `List<RdPath>`, which `JetBrains.IDE.PathExtensions.ToRd(VirtualFileSystemPath)` makes (`Disk/Rescans.cs`).
`RdCall.Start` asserts the protocol thread: call it inside `IShellLocks.Tasks.StartNew(lifetime, Scheduling.MainGuard, ...)`.

### `IPsiFiles.AllDocumentsAreCommittedConstraint`, not `AllDocumentsAreCommittedReadConstraint`
That class is internal since Rider 2026.3: built against the 2025.3 SDK, constructing it failed every tool call with a
`MethodAccessException` in `McpToolExecution`'s constructor. The public constraint also requires that no PSI
transaction is running (`IPsiTransactions.NoPsiTransactionConstraint`).

### Position resolution near string literals
Asking a string literal for references runs ReSharper's language-injection analysis, which NREs in
`GetRuntimeFeatures` without an ambient `CompilationContextCookie`. `PsiHelpers.ResolveAt` sets the cookie, skips
literal nodes (`PsiHelpers.IsLiteral`), and falls back to resolving the identifier text by name.

### Decompiled sources
`DecompiledSource` calls `IMetadataTranslator.TranslateTopLevelTypeElement` with the user's `TranslatorOptions`.
Method bodies only when `DecompilerSettings.DecompilerLegalNoticeAccepted` is set; otherwise a metadata view.
Never show the notice from a tool (it is a modal `MessageBox`).

### Small API traps
- `IPsiSourceFile.PsiModule` is a property; there is no `GetPsiModule()` extension for it.
- `psiFile.Descendants().OfType<T>()` returns a struct enumerator, not `IEnumerable<T>`: `foreach` it, no LINQ.

### Daemon API
`IDaemon` has no public "get current highlightings" API. For file errors, walk the PSI tree:
- `IErrorElement` nodes for syntax errors
- `reference.Resolve().ResolveErrorType` for unresolved references
`ResolveErrorType` is in `JetBrains.ReSharper.Psi.Resolve`.

## File Map

```
CLAUDE.md                              # this file
README.md                              # user-facing docs
.editorconfig                          # code style, as in Deskmancer and Compze: 3-space indents, no default modifiers
CHANGELOG.md                           # change notes, Keep a Changelog: the Marketplace's and the GitHub Release's
.github/workflows/                     # build.yml on every push; release.yml on a v* tag: sign, release, publish
rider-plugin/build.gradle.kts          # the build: compileDotNet + prepareSandbox put the .NET part beside the jar
ReSharperMcp.sln                       # solution file (renamed with the clone's folder, later)
rider-plugin/META-INF/plugin.xml       # IntelliJ plugin descriptor
dev_docs/swappable-tools.md            # the host/tools split: how swapping works, the rules for tools code
src/DotNetAnalysisAndRefactoringMcp/   # THE HOST: what Rider loads; a change here needs a Rider restart
  DotNetAnalysisAndRefactoringMcp.csproj  # targets net472, refs JetBrains.ReSharper.SDK 2026.3.0
  McpShellComponent.cs                 # ShellComponent — binds a port, owns the HTTP server; watchdog, peer takeover
  McpServerComponent.cs                # SolutionComponent — holds what of a solution outlives a tools build
  McpHttpServer.cs                     # HttpListener, event streams, registrations; hands each request to the tools
  PeerRegistration.cs                  # A peer's solution registered with the primary: its port, its tools as JSON
  SolutionRoot.cs                      # Where relative paths start: solution directory and the work tree above it
  DaemonHighlightingCollector.cs       # Runs the daemon on a file; in the host because ReSharper keeps its type
  Threading/                           # IThreadShared: state shared between threads, reached only inside its lock; the one raw lock
  Swapping/
    SwappableTools.cs                  # ShellComponent — which tools build is served; swaps; checks freed
    LoadedToolsBuild.cs                # One build in its own load context; counts calls; ToolsLease
    ToolsBuildFolder.cs                # A folder of builds: the newest one for this host
    CollectibleLoadContext.cs          # AssemblyLoadContext through reflection (net472 has none)
  Disk/
    DiskChanges.cs                     # FileSystemWatcher: source files changed since last taken
    DiskSyncState.cs                   # What the disk sync remembers between calls: stale and absent files
  Tools/                               # what the tools implement, and the host's helpers for calls
    IMcpToolsEntryPoint.cs             # The way into a tools build: answers a request, the health, registers with the primary
    IMcpServer.cs                      # The server as one request sees it: registrations, and what only the host can do
    IMcpSolution.cs                    # An open solution as one request sees it: what of it outlives a tools build
    ToolFailure.cs                     # One-line crash text
src/DotNetAnalysisAndRefactoringMcp.Tools/  # THE TOOLS: swapped in between calls; same namespaces as before the split
  McpToolExecution.cs                  # Interruptible PSI reads and read-then-main-thread continuations
  Indexing.cs                          # ReSharper's indexing of a solution: whether it runs, a wait until it's done
  JsonOf.cs                            # JSON of the tools' objects without Newtonsoft's type caches, [JsonProperty] names
  Protocol/
    JsonRpc.cs                         # JSON-RPC request/response/error types, read and answered
    McpTypes.cs                        # MCP types: InitializeResult, ToolDefinition, etc.
  Serving/
    McpRequest.cs                      # One JSON-RPC request and its answer, by method
    ToolList.cs                        # tools/list: the solutions' tools with solutionName, after list_solutions
    OpenSolutions.cs                   # The solutions a call is routed to; list_solutions; /health
    OpenSolution.cs                    # One solution, by name, path segment or the files it holds
    LocalSolution.cs                   # One of this Rider's: its tools answer the call
    PeerSolution.cs                    # One of a peer's: the call is forwarded to the peer
    PrimaryServer.cs                   # A peer registering its solutions with the primary
    OtherMcpServer.cs                  # Another Rider's MCP server, reached at its port
  PsiHelpers.cs                        # Shared: file lookup, name/position resolution, suggestions, snippets
  SymbolName.cs                        # Parses "Ns.Type.Member<T>(...)" as agents write it
  CompilationContexts.cs               # Ambient CompilationContextCookie of an element's own file
  NamespaceFolders.cs                  # The directory of a namespace's files in a project, by the folders' expected namespaces
  SkippedElements.cs                   # Per-element guard for walks + the "skipped N elements" line
  McpInstructions.cs                   # Usage guide sent in the initialize response (from the tools, so no restart)
  Severities.cs                        # "error"/"warning"/... to and from ReSharper's Severity, and their order
  SuperMembers.cs                      # The members a member overrides/implements: what find_usages and call hierarchy follow
  DeclarationSite.cs                   # Where a declaration is: the file, line and column of its name (not its doc comment)
  DecompiledSource.cs                  # ReSharper decompiler / metadata view for compiled types
  Disk/
    GitChanges.cs                      # Files changed since a git ref (for get_file_errors changedSince)
    DiskSync.cs                        # Reconcile + rescan + wait until Rider's copy matches disk, before every call
    DiskText.cs                        # Whether a file on disk holds a document's text (line endings, BOM aside)
    UnsavedDocuments.cs                # An edit's documents saved before the answer: through Rider's frontend, waited for
    DocumentNotSaved.cs                # One that Rider hadn't saved in time, and how its file differed, for the note
    FileMoves.cs                       # Files an edit moves, moved on disk after the save; FileMove.cs is one of them
    Rescans.cs                         # Rider's frontend asked to rescan paths (FileSystemModel.RefreshPaths)
  Tools/
    ToolsEntryPoint.cs                 # Answers the host's requests; the host knows it by name
    McpTools.cs                        # The tools a solution is served with, created anew for every request
    FileArguments.cs                   # filePath / filePaths / symbols[].filePath of a call
    ToolCall.cs                        # One call's flow: disk sync, stale-file refusal, dispatch by kind, timeout, notes
    IMcpTool.cs                        # Tool interface: Name, Description, InputSchema, Execute
    IMcpWriteTool.cs                   # Marker interface for write tools (write lock + PsiTransaction)
    IMcpEditTool.cs                    # Markers: edits source files (stale guard); expands arguments
    IMcpAsyncTool.cs                   # Read phase, then mutation phase, no lock held in between
    ToolBatch.cs                       # Batch runner with per-item isolation; ToolText
    FindUsagesTool.cs                  # find_usages — IFinder.FindReferences()
    GetSymbolInfoTool.cs               # get_symbol_info — detailed symbol metadata
    FindImplementationsTool.cs         # find_implementations — FindInheritors/FindImplementingMembers
    GetFileErrorsTool.cs               # get_file_errors — PSI tree walk for errors
    SearchSymbolTool.cs                # search_symbol — substring search across declarations
    GoToDefinitionTool.cs              # go_to_definition — navigate to symbol declaration
    GetSolutionStructureTool.cs        # get_solution_structure — projects, TFMs, references
    BrowseNamespaceTool.cs             # browse_namespace — namespace hierarchy exploration
    ListSymbolsInFileTool.cs           # list_symbols_in_file — all declarations in a file
    FixUsingsTool.cs                   # fix_usings — add missing C# using directives
    FlowTool.cs                        # flow — control-flow summary with branch/loop/call inlining
    FormatFileTool.cs                  # format_file — CodeCleanupRunner (self-transacting write tool)
    GetSymbolSourceTool.cs             # get_symbol_source — full declaration source text
    GetCallHierarchyTool.cs            # get_call_hierarchy — incoming/outgoing call tree
    GetTypeHierarchyTool.cs            # get_type_hierarchy — supertype/subtype tree
    GetDiagnosticsTool.cs              # get_diagnostics — daemon inspections with quick-fix availability
    InspectCodeTool.cs                 # inspect_code — Inspect Code's engine over a scope, local or with SWEA's results
    ListQuickFixesTool.cs              # list_quick_fixes — bulb actions at a position
    CompleteAtTool.cs                  # complete_at — code completion suggestions (main-thread dispatch)
    RenameSymbolTool.cs                # rename_symbol — semantic solution-wide rename (self-transacting write)
    MoveTypeToNamespaceTool.cs         # move_type_to_namespace — Move to Another Namespace, checked, extension usings, file moved
    GenerateMembersTool.cs             # generate_members — generate type members (write)
    ApplyQuickFixTool.cs               # apply_quick_fix — apply a bulb action (self-transacting write)
    ApplySuggestionsTool.cs            # apply_suggestions — file-wide scoped quick-fixes by inspection id
    ReloadProjectsTool.cs              # reload_projects — ISolutionHost.ReloadProjectsAsync, waited for via LoadEvents
tools/                                 # development tools: the sandbox Rider, deploying (see .claude/rules)
tests/DotNetAnalysisAndRefactoringMcp.Specifications/  # the tools checked in the sandbox Rider
tests/FixtureSolution/                 # the solution the specifications open there
```

## Building & Installing

`cd rider-plugin && ./gradlew buildPlugin` builds the plugin zip, .NET part included. On Magnus's machine, use the
development tools to install and deploy (see `.claude/rules/02-universal-local/developing-the-plugin.md`): they test
in the sandbox Rider and deploy tools builds that take effect without a restart.

Installing into a running Rider: move the old files aside or delete them first, never copy over them in place. A
running backend has the DLL mapped, and copying over it corrupted the mapped image and crashed the backend ("Bad IL
range" on a watcher thread).

Override port: `DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT=9999`

## Publishing

Released as the .NET Terminal Symbol Linker is: `pluginVersion` in `rider-plugin/gradle.properties`, the version's
section in `CHANGELOG.md`, then a `v<version>` tag, which `.github/workflows/release.yml` builds, signs, releases on
GitHub and publishes to the Marketplace. Publishing needs Magnus's word. The repository's secrets hold the signing
certificate and the Marketplace token; `MARKETPLACE_ID` in `release.yml` is the plugin's Marketplace id, which its
first upload, by hand, gave it.

## Testing

```bash
# Initialize handshake
curl -s http://127.0.0.1:23751/ -X POST -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"capabilities":{}}}'

# List tools
curl -s http://127.0.0.1:23751/ -X POST -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'

# Find usages
curl -s http://127.0.0.1:23751/ -X POST -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"find_usages","arguments":{"filePath":"/path/to/File.cs","line":10,"column":18}}}'

# Get symbol info
curl -s http://127.0.0.1:23751/ -X POST -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_symbol_info","arguments":{"filePath":"/path/to/File.cs","line":10,"column":18}}}'

# Find implementations
curl -s http://127.0.0.1:23751/ -X POST -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"find_implementations","arguments":{"filePath":"/path/to/IFoo.cs","line":5,"column":18}}}'

# Get file errors
curl -s http://127.0.0.1:23751/ -X POST -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"get_file_errors","arguments":{"filePath":"/path/to/File.cs"}}}'

# Search symbols by name
curl -s http://127.0.0.1:23751/ -X POST -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"search_symbol","arguments":{"query":"Player","maxResults":10}}}'

# Go to definition (by name)
curl -s http://127.0.0.1:23751/ -X POST -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"go_to_definition","arguments":{"symbolName":"MyClass"}}}'

# Find usages by symbol name (no need for file:line:col)
curl -s http://127.0.0.1:23751/ -X POST -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":9,"method":"tools/call","params":{"name":"find_usages","arguments":{"symbolName":"IConnection"}}}'

# Find implementations by symbol name
curl -s http://127.0.0.1:23751/ -X POST -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":10,"method":"tools/call","params":{"name":"find_implementations","arguments":{"symbolName":"IConnection"}}}'

# List open solutions (multi-solution support)
curl -s http://127.0.0.1:23751/ -X POST -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":11,"method":"tools/call","params":{"name":"list_solutions","arguments":{}}}'

# Target a specific solution (when multiple are open)
curl -s http://127.0.0.1:23751/ -X POST -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":12,"method":"tools/call","params":{"name":"search_symbol","arguments":{"query":"Player","maxResults":10,"solutionName":"MyProject"}}}'
```

## Next Steps / Ideas

- SSE support for streaming results on large solutions
- NuGet packaging / JetBrains Marketplace distribution
- GitHub CI with release artifacts
- Proper Rider plugin with settings UI (port config, enable/disable)
### Symbol name resolution (`PsiHelpers.ResolveSymbolByName`)
Walks all PSI files looking for declarations matching the given name. Supports:
- Short names: `"MyClass"` → first match
- Qualified names: `"Namespace.MyClass"` → exact FQN match, falls back to short name match
- Nested types: `"MyClass.InnerType"`
- Skips `INamespace` elements to avoid noise
