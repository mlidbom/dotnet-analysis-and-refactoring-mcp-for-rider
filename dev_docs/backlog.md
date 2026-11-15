# Backlog: what the plugin should do better

What Claude found the plugin lacking, or would build next, most valuable first. Sessions in other projects add what
they run into here (see the user-level rule that the dotnet-analysis-and-refactoring MCP is a development tool you own). An item that is done goes from the list.

## Tools

1. **`move_type_to_namespace` moving the file too,** to the folder of its new namespace. It moves the declaration,
   references and usings, and gives extension-method callers their using, but leaves the file: Rider's documents hold
   the unsaved result, and a file moved on disk under them would lose it. ReSharper's Move to Folder refactoring may
   do it inside the model.
2. **Edit tools saving what they changed before they answer.** Rider saves the documents a refactoring changed a
   while later, so an agent that builds at once builds the old text (the move specification waits for the save). The
   protocol has an `RdSaveAllRequest` (Rider's `RdRequests`), which may be the way to ask the frontend to save.

## Code style

The host's files (`src/DotNetAnalysisAndRefactoringMcp/`) still have upstream's style: reformatting them changes the
host's build, which takes a Rider restart. Format them, as `format_file` and `apply_suggestions` with
`ArrangeTypeMemberModifiers,ArrangeNamespaceBody` did the rest on 2026-11-15, with the host's next real change.

## Swapping the tools without a restart

See `dev_docs/swappable-tools.md`.

1. **Faster specification runs when only the tools changed:** swap the build into the running sandbox instead of
   restarting it with a fresh copy of the fixture solution.

## Known bugs

- `search_symbol` with `includeLibraries` finds a compiled member only as `Type.Member`, not by its name alone
  (`GetProjectMark` gives none): scanning every compiled type's members takes seconds. An index of member names
  would do it.
- `rename_symbol` of a parameter missed a `<paramref>` to it (seen 2026-11-10). Didn't reproduce on 2026-11-14: renaming
  a parameter by the position of its declaration renamed its `<paramref>` too. Unknown what differed; note how it was
  called if it happens again.

- `apply_suggestions` at `scope` folder, project or solution changes nothing for the code-style fixes
  (`ArrangeTypeMemberModifiers`, `ArrangeNamespaceBody`, seen 2026-11-15), yet answers that it applied them. At scope
  file they work, and `RedundantUsingDirective` works at every scope. Probably their fix-in-scope runs code cleanup over
  the scope, which ours doesn't drive. Whatever the cause, the answer should say when a fix changed no file.
- `inspect_code` with solution-wide analysis reports that analysis's stored results, which lag a change of settings:
  after `.editorconfig` made `private` redundant, `get_diagnostics` flagged it at once, `inspect_code` not yet.
- A `move_type_to_namespace` dry run answered with the host's "(index still building — results may be incomplete)",
  probably because rolling back its temporary transaction updates the caches. `/health` said idle right after.

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
