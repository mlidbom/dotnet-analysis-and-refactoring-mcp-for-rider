# Backlog: what the plugin should do better

What Claude found the plugin lacking, or would build next, most valuable first. Sessions in other projects add what
they run into here (see the user-level rule that the dotnet-analysis-and-refactoring MCP is a development tool you own). An item that is done goes from the list.

## Tools

1. **Moving a type to another namespace, completely, in one call:** the namespace declaration, the file and its
   folder, the `using` directives, and extension-method call sites. The rider MCP's `move_type_to_namespace` sometimes
   reports success and leaves the declaration on the old namespace, adds no `using` for extension-method callers, and
   doesn't move the file (the user-level rule on automated C# refactoring lists the routine that works around it).

## Swapping the tools without a restart

See `dev_docs/swappable-tools.md`.

1. **Faster specification runs when only the tools changed:** swap the build into the running sandbox instead of
   restarting it with a fresh copy of the fixture solution.

## Known bugs

- `search_symbol` with `includeLibraries` finds a compiled type by part of its name, but not by its qualified name
  (`JetBrains.Application.Progress.ProgressIndicator` gives none), and a compiled member only as `Type.Member`, not
  by its name alone (`GetProjectMark` gives none). `get_symbol_info` resolves the qualified name (seen 2026-11-15).
- `rename_symbol` of a parameter missed a `<paramref>` to it (seen 2026-11-10). Didn't reproduce on 2026-11-14: renaming
  a parameter by the position of its declaration renamed its `<paramref>` too. Unknown what differed; note how it was
  called if it happens again.

## Bigger ideas

- **Build against the SDK of the Rider version Magnus runs, and drop older ones,** once the fork is renamed: no more
  reflection to bridge versions, and an API change becomes a compile error instead of a run-time failure such as
  issue #8's `MethodAccessException`.
- **An agent Rider:** a second Rider, separate from Magnus's, serving every session's code intelligence. A change to
  the host would need no restart of his IDE (tools builds need none already), and a broken build couldn't hang it. Costs memory and a second index of each
  solution.
- **Hosting the plugin in a headless ReSharper process,** as `jb inspectcode` hosts the engine: code intelligence
  with Rider closed, in cloud sessions and in CI. Uncertain: the plugin makes some calls only Rider's frontend
  answers, such as the file rescans in `Disk/RefreshRequests.cs`.
