# Backlog: what the plugin should do better

What Claude found the plugin lacking, or would build next, most valuable first. Sessions in other projects add what
they run into here (see the user-level rule that the dotnet-analysis-and-refactoring MCP is a development tool you own). An item that is done goes from the list.

## Known bugs

- **A call by name right after a file is created on disk doesn't find its types:** the disk sync waits for a new
  file only when the call names it, so `search_symbol`, or a tool given a `symbolName`, answers "Symbol not found" until
  Rider's file watcher has taken the file in. In a loop that wrote a file and moved its types by name 2.5 s later, 1 of
  40 calls found nothing (2026-11-15). Waiting a moment for the files `DiskChanges` saw created, as for named ones,
  would do it.
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

- A directory `DiskChanges` fails to watch is never tried again: each is watched once, so edits under it are noticed
  only in files a call names. Retrying each call would log the failure each call; retrying with a backoff would
  do it. Seen in no log yet (the catch audit, 2026-11-15).

## Bigger ideas

- **Hosting the plugin in a headless ReSharper process,** as `jb inspectcode` hosts the engine: code intelligence
  with Rider closed, in cloud sessions and in CI. Uncertain: the plugin makes some calls only Rider's frontend
  answers, such as the file rescans in `Disk/Rescans.cs`.

## Decided against

- **An agent Rider,** a Rider of its own per solution for the agents, so that a host change needs no restart of
  Magnus's (2026-11-15). He has five or six Riders open: one more for each would cost a lot of processor time and memory,
  be a hassle to handle, and break his workflow. That the agent works in the window he works in, and sees what he sees,
  is worth more.
- **Specification runs without restarting the sandbox,** when only the tools changed (2026-11-15). Measured: a run is
  67 s, and stopping and starting the sandbox, with the fresh copy's restore, about 20 s of it. It would add a second
  way to run, and weaken the check that ReSharper keeps nothing of the tools build that first ran each feature, which
  takes a fresh process.
