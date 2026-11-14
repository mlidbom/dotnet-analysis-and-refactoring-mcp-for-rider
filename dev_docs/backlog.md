# Backlog: what the plugin should do better

What Claude found the plugin lacking, or would build next, most valuable first. Sessions in other projects add what
they run into here (see the user-level rule on resharper-joshua). An item that is done goes from the list.

## Tools

1. **Inspections over a solution, project or folder, from the live engine.** No MCP tool gives solution-wide
   warnings today, so whole-solution sweeps go through the headless `jb inspectcode`, which is slow. Rider has the
   results; nothing hands them over.
2. **Moving a type to another namespace, completely, in one call:** the namespace declaration, the file and its
   folder, the `using` directives, and extension-method call sites. The rider MCP's `move_type_to_namespace` sometimes
   reports success and leaves the declaration on the old namespace, adds no `using` for extension-method callers, and
   doesn't move the file (the user-level rule on automated C# refactoring lists the routine that works around it).
3. **`reload_projects`:** reload a project whose Rider model is stale, such as a new view's code-behind that reports
   `InitializeComponent` unresolved. Replaces `~/.claude/tools/rider_reload_projects.py`, which edits a gitignored
   `nuget.g.props` and has the rider MCP lint it to make Rider reload.
4. **Fix-all for one inspection over a scope.** `apply_suggestions` works one file at a time; cleanup sweeps, such as
   removing redundant `using` directives after a move, need a project or the solution.

## Known bugs

- `rename_symbol` given the position of a `using var` line took the implicit `Dispose` call and renamed the type's
  `Dispose` method (seen 2026-11-10).
- `rename_symbol` of a parameter missed a `<paramref>` to it (seen 2026-11-10).
- `get_call_hierarchy` on a concrete method returns nothing when every caller calls it through an interface; asked
  about the interface member it finds them.
- `get_symbol_info` and `go_to_definition` put a spurious `static` in a type's header: a sealed class shows as
  "static class", an interface as "static abstract interface" (seen again 2026-11-14 on JAStudio's `LocalNoteUpdater`).

## Bigger ideas

- **Build against the SDK of the Rider version Magnus runs, and drop older ones,** once the fork is renamed: no more
  reflection to bridge versions, and an API change becomes a compile error instead of a run-time failure such as
  issue #8's `MethodAccessException`.
- **An agent Rider:** a second Rider, separate from Magnus's, serving every session's code intelligence. Deploying a
  build would need no restart of his IDE, and a broken build couldn't hang it. Costs memory and a second index of each
  solution.
- **Hosting the plugin in a headless ReSharper process,** as `jb inspectcode` hosts the engine: code intelligence
  with Rider closed, in cloud sessions and in CI. Uncertain: the plugin makes some calls only Rider's frontend
  answers, such as the file rescans in `Disk/RefreshRequests.cs`.
