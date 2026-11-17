# Backlog: what the plugin should do better

What Claude found the plugin lacking, or would build next, most valuable first. Sessions in other projects add what
they run into here (see the user-level rule that the dotnet-analysis-and-refactoring MCP is a development tool you own). An item that is done goes from the list.

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

- A directory `DiskChanges` fails to watch is never tried again: each is watched once, so edits under it are noticed
  only in files a call names. Retrying each call would log the failure each call; retrying with a backoff would
  do it. Seen in no log yet (the catch audit, 2026-11-15).

- `get_symbol_info` with `includeMembers` lists a compiled type's C# 14 extension blocks as the static methods their
  members compile to, such as `get_IsLogicallyStatic(typeExtension:ITypeExtension)` for ReSharper's own
  `ITypeExtensionEx`, and not the block at all. A source type's blocks are listed as C# declares them (`MemberListing`).
  ReSharper models a compiled block as an `ICompiledTypeExtension`, reached through its grouping and marker types,
  whose `FindDeclaringTypeMember` gives the member a static method was compiled from.

- **Rider saved its stale copy of a file over the newer text on disk, and the note before it didn't warn**
  (taggregates-opus, JAStudio.wt2, 2026-11-17). For about 25 minutes every `get_diagnostics` call, whatever file it
  named, ended with "note: Rider's copy of …\JPCollection.cs differs from disk (unsaved edits in Rider, or not reloaded
  yet)". The session edited and committed that file on disk meanwhile. At 17:07:32 Rider closed the file and saved its
  copy, from before all those edits, over it: about an hour's work, back from the commits only because they held it.
  The cause, in Rider's `idea.log`: at 16:19:57, right after Rider started, an `EditorPaste` put "claude --resume" into
  that file's editor, a paste meant for a terminal. The document then held unsaved edits, and Rider kept them over
  every change on disk until the tab closed. The owner's call on what the plugin does:
  1. **Tell the two cases apart:** unsaved edits in Rider, and a file Rider hasn't reloaded yet, which passes in
     seconds. The note now names both as one.
  2. **For unsaved edits, say what is at stake, and show them:** "Rider holds unsaved edits to X: its next save of X,
     such as when its tab closes, writes them over what is on disk now. Ask Magnus to save or revert them in Rider
     before you edit X on disk", with the diff of Rider's copy against the disk. Here the diff would have shown the
     pasted "claude --resume" at once.
  3. **A tool that reverts Rider's copy to the disk,** asked for by the session: only on Magnus's word, as its
     description says, since the unsaved edits may be his.

- The development tools' `peer stop` timed out after 2 minutes once (2026-11-17): the peer sandbox Rider showed a
  "Confirm Exit" dialog, on every virtual desktop, which closing its windows doesn't answer. Exit confirmation is off in
  its settings, so it was another reason, likely a process it ran (it had started GitHub Copilot's agent through npx).
  The specification run before and after passed. Stopping should answer or report such a dialog, not wait it out.

- **`apply_suggestions` applies nothing for an inspection of solution-wide analysis** (the owner, ClaudeTooling,
  2026-11-17). `inspect_code` listed 54 `MemberCanBePrivate.Global` warnings, with solution-wide analysis complete;
  `apply_suggestions` with that id, at file or solution scope, answered "nothing to apply" in a file that had one, so
  they were made private by hand. Its fixes are found through the file's daemon, which doesn't run the solution-wide
  inspections. Rider's own Fix in solution does apply them. Either apply them, or say why there was nothing, as for
  the stale results above.

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
