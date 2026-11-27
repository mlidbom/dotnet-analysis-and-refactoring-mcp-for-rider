# .NET Analysis&Refactoring MCP

A fork of Joshua Light's [MCP Server for Code Intelligence](https://github.com/joshua-light/resharper-mcp), renamed so that it can be developed and released on its own and installed beside the original.

Royder exposes ReSharper navigation, inspections and refactorings to AI assistants over MCP. With the standalone
primary installed on Windows, call a tool directly with an absolute `filePath` or a full `.sln` or `.slnx` path
as `solutionName`. It opens the trusted checkout on demand, even with desktop Rider closed.

Supports C#, F#, VB, and any language with a ReSharper PSI implementation.

## Tools

| Tool | Description |
|------|-------------|
| `find_usages` | Find all references to a symbol; rg-shaped `path` / `line: code` output, `mode: "files"`, `pathPrefix`, `excludeTests` |
| `get_symbol_info` | Detailed symbol info: kind, type, params, docs, base types, members |
| `find_implementations` | Find implementations of interfaces/abstract classes and overrides |
| `get_file_errors` | Fast compile check: syntax errors and unresolved references; `changedSince: "<git ref>"` checks every changed file |
| `search_symbol` | Search symbols by name (substring match) across the solution; `includeLibraries` adds referenced DLLs |
| `go_to_definition` | Navigate to a declaration. Text replies include up to 200 source characters by default. Set `maxTextLength` for a longer excerpt or use `get_symbol_source` for the declaration. |
| `get_solution_structure` | List projects, target frameworks, and project references |
| `browse_namespace` | Browse namespace hierarchy: child namespaces and types |
| `list_symbols_in_file` | List all declarations in a file |
| `list_solutions` | List all open solutions across Rider instances |
| `fix_usings` | Fix missing using directives in C# files |
| `format_file` | Format, clean up, or apply code style to a file |
| `flow` | Describe control flow of a method or type: execution steps, branches, loops, error paths, inlined call targets |
| `get_symbol_source` | Get the full declaration source code of a symbol; decompiled source for symbols compiled into DLLs |
| `get_call_hierarchy` | Build an incoming (callers) or outgoing (callees) call hierarchy tree for a method |
| `get_type_hierarchy` | Get the inheritance hierarchy of a type: supertypes (base/interfaces) or subtypes |
| `get_diagnostics` | Run daemon inspections on a file; reports severity, inspection id, message, location, and quick-fix availability |
| `list_quick_fixes` | List the ReSharper quick-fixes (bulb actions) available at a position |
| `complete_at` | Get code completion suggestions at a caret position |

Write tools (modify source — require a write lock):

| Tool | Description |
|------|-------------|
| `rename_symbol` | Semantic, solution-wide rename of a symbol and all its references, and of a type's file named after it (supports `dryRun`) |
| `generate_members` | Generate members on a type (constructors, overrides, equality members, etc.) |
| `apply_quick_fix` | Apply a ReSharper quick-fix (bulb action) at a position |
| `apply_suggestions` | Apply inspection quick-fixes file-wide by inspection id (e.g. convert explicit constructor → primary constructor); position-free, `dryRun`/`all` supported |

### Symbol resolution

Tools that operate on a symbol accept two modes:
- **By position** — `filePath` + `line` + `column` (1-based)
- **By name** — `symbolName` (e.g. `"MyClass"`, `"MyClass.MyMethod"`, `"Namespace.MyClass"`, `"Wait<M>"`, `"Execute(int)"`)

Names are looked up in sources first, then in referenced assemblies (types and `Type.Member`), then in sources ignoring case. An optional `kind` filter (`"type"`, `"method"`, `"property"`, `"field"`, `"event"`) helps disambiguate. When multiple symbols match, tools return an ambiguity error listing all candidates with their qualified names, kinds, and locations. When nothing matches, the error lists up to five similar names.

File paths may be absolute, or relative to the repository root or the solution's folder.

### Files edited outside Rider

Before every call the server checks the files the call names, and the files it saw change on disk, against Rider's copy. A stale copy is reconciled with disk the way Rider does when its own file watcher reports a change, and Rider is asked to rescan, so a new file joins its project. A file open in an editor tab reloads at Rider's pace; the call waits up to 10 s for it. A copy with unsaved edits in Rider is never touched; the result notes that it reflects Rider's copy.

Edit tools (`fix_usings`, `format_file`, `rename_symbol`, `generate_members`, `apply_quick_fix`, `apply_suggestions`) refuse to run while Rider's copy of a named or changed file still differs from disk, because the edit would be applied to the old text and saved over the new one. `dryRun: true` calls are never refused.

### Failures

- A type or member ReSharper cannot read is skipped, and the result ends with `skipped N elements ReSharper failed to read (...)`.
- In a batch, a failing item reports one error line under its header; the other items still answer.
- A crash is reported as one line (`ReSharper failed on <tool>: <ExceptionType>: <message>`). The stack trace goes to the Rider log.

### Batch mode

Most tools support batch mode — processing multiple inputs in a single call. This reduces round-trips when querying several symbols or files at once:

- **Symbol-based tools** (`find_usages`, `get_symbol_info`, `find_implementations`, `go_to_definition`) accept a `symbols` array of `{symbolName, kind, filePath, line, column}` objects.
- **File-based tools** (`get_file_errors`, `list_symbols_in_file`, `fix_usings`, `format_file`) accept a `filePaths` array of strings.
- **`search_symbol`** accepts a `queries` array of strings.
- **`browse_namespace`** accepts a `namespaceNames` array of strings.

Results are concatenated with `=== [N/total] label ===` separators. Shared options (e.g. `maxResults`, `kinds`, `mode`) apply to all items in the batch. Original single-input parameters remain for backward compatibility.

### Health check

`GET http://127.0.0.1:23751/health` answers plain JSON without an MCP session, e.g. for a session-start hook:

```json
{"solutions":[{"name":"TPS.All","path":"/…/TPS.All.sln","indexing":false,"sourceGeneratorsReady":true,"pendingDiskChanges":0}],"indexing":false,"pendingDiskChanges":0}
```

When a call's work begins while ReSharper is indexing, its result ends with `(index still building — results may be incomplete)`. The `initialize` response carries a short usage guide in `instructions`.

For a call that needs generated declarations, check the **target solution's** `indexing` and
`sourceGeneratorsReady` fields. Wait for `indexing: false` and a readiness result of `true` or `null`.

| `sourceGeneratorsReady` | Meaning |
|---|---|
| `true` | Rider has synchronized its Roslyn project model and ReSharper has processed the applicable projects' source-generator results. An analyzer-only project can return this after its empty generation result. |
| `null` | The loaded solution has no analyzer references in a C# or VB project that supports source generators. |
| `false` | Readiness is pending or could not be confirmed. A reload or a busy project model can return this again. |

A missing field on an older or unreachable peer means readiness is unknown. On a readiness read failure,
`sourceGeneratorHealthFailed` gives the error alongside `sourceGeneratorsReady: false`.
Rider publishes worker state asynchronously. Treat health as an observation at startup. Edits, reloads and worker
restarts can change readiness between a health response and the next tool call.

## Installation

Requires Rider 2026.3 or later. Rider 2025.3 to 2026.2 get version 0.13.0 from the Marketplace.

### From JetBrains Marketplace

Install the plugin from Rider: **Settings → Plugins → Marketplace** → search for ".NET Analysis&Refactoring MCP".

### From a release

Download the signed zip from the repository's
[Releases](https://github.com/mlidbom/dotnet-analysis-and-refactoring-mcp-for-rider/releases), then in Rider:
**Settings → Plugins → ⚙ → Install Plugin from Disk…**, and restart Rider.

## MCP client configuration

Add to your MCP client config (e.g. Claude Code `settings.json`):

```json
{
  "mcpServers": {
    "dotnet-analysis-and-refactoring": {
      "type": "http",
      "url": "http://127.0.0.1:23751/"
    }
  }
}
```

With only the Rider plugin installed, the server starts when you open a solution in Rider.

Set `DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT` environment variable to override the default port.

File arguments pick a solution in their own Git work tree, even when only one solution is open. Files in a nested work tree need a solution there. A relative path shared by several open solutions needs an absolute path or `solutionName`. You can also select a solution explicitly for a linked file outside its work tree.

Of several solutions in the work tree, a call goes to one that lists the files' projects, and of those, to the one in the deepest folder holding them all, then to the one listing the most projects. The standalone primary opens one by the same order. A repository can name the solution for its files in a `.royder.json` at its root, a path relative to that root, which wins when that solution lists the files' projects:

```json
{"solution": "Compze.AllProjects.slnx"}
```

### On-demand worktrees on Windows

On Windows, the standalone Royder primary opens a trusted repository's main checkout or any of its linked worktrees
on demand in one private Rider JVM. Agents work on the real source files. The private JVM keeps its IntelliJ project
settings outside the checkout, and ordinary Rider takes over when you open the solution.
[Build, configure and run the standalone primary](dev_docs/headless-royder/standalone-primary.md)

## Building

```bash
cd rider-plugin
./gradlew buildPlugin   # builds the .NET part (host and tools) and the jar; the zip lands in build/distributions/
```

Gradle needs a JDK 21 (it provisions one through the toolchain resolver) and the .NET SDK.

## Releasing

As the [.NET Terminal Symbol Linker](https://github.com/mlidbom/RiderTerminalSymbolLinker) is released: update
`pluginVersion` in `rider-plugin/gradle.properties` and the version's section in `CHANGELOG.md`, then push a tag:

```bash
git tag v0.13.1 && git push origin v0.13.1
```

`.github/workflows/release.yml` builds and signs the plugin, creates the GitHub Release with the change notes, and
publishes the version to the JetBrains Marketplace.

## Architecture

- Runs as a ReSharper `SolutionComponent` (activated when a solution opens, stopped when it closes)
- Hosts an HTTP server on `127.0.0.1:23751` implementing MCP over JSON-RPC 2.0
- Uses ReSharper's PSI (Program Structure Interface) APIs for code analysis
- Two-part Rider plugin: minimal JVM JAR (plugin descriptor) + .NET backend DLL
- Targets `net472` (required by the ReSharper host process)
