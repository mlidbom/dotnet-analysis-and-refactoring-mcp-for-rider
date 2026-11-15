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

- Solution-wide analysis's stored results lag a change of settings, and two tools read them: `inspect_code`, and
  `apply_suggestions` at a wider scope, whose fix skips the files in which those results show no such issue
  (`ScopedActionExecutor`, when `MakeUseOfSolutionAnalysisResultsInScopedIntentions` is on, as by default). After a new
  `.editorconfig` made `private` redundant (2026-11-15), `get_diagnostics` flagged it at once, `inspect_code` reported
  none 20 minutes later, and a fix in the solution changed no file, not even the one it was found in. Rider's own Fix in
  solution would do the same. `apply_suggestions` now says how many files a fix changed, and why none may have changed.
  A fix: re-run the analysis, `SolutionAnalysisManager.ReanalyzeAll()`, which takes minutes on a large solution, so
  only when asked, such as a `reanalyze` argument to `inspect_code`.
- A `move_type_to_namespace` dry run answered with the host's "(index still building — results may be incomplete)",
  probably because rolling back its temporary transaction updates the caches. `/health` said idle right after.
- `get_file_errors` reports "Cannot resolve symbol 'Deconstruct' (unverified: may be source-generated)" on tuple
  deconstructions that compile, such as `var (offset, positionError) = …` in `GetDiagnosticsTool.cs`: `changedSince:
  0eee149` on this repository gave 43 such errors in 20 files that build, where `get_diagnostics` gives none. The
  usage guide calls it "the fast compile check", so agents trust it. A tuple's deconstruction has no `Deconstruct`
  method to resolve to; the reference walk should skip it. Found by the shared-rules session, 2026-11-15.
- A `rename_symbol` dry run of `IMcpTool.Execute` listed 2 changed files, `IMcpTool.cs` and `ToolCall.cs`, while about
  25 tools implement it. Unconfirmed: the answer also had the "index still building" note above, so it may be that, the
  changed-files list under-reporting, or the rename not following the implementations. Found by the shared-rules
  session, 2026-11-15.

- A directory `DiskChanges` fails to watch is never tried again: each is watched once, so edits under it are noticed
  only in files a call names. Retrying each call would log the failure each call; retrying with a backoff would
  do it. Seen in no log yet (the catch audit, 2026-11-15).

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
