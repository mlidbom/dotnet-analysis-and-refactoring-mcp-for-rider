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

- **`apply_suggestions` applies nothing for an inspection of solution-wide analysis** (the owner, ClaudeTooling,
  2026-11-17). `inspect_code` listed 54 `MemberCanBePrivate.Global` warnings, with solution-wide analysis complete;
  `apply_suggestions` with that id, at file or solution scope, answered "nothing to apply" in a file that had one, so
  they were made private by hand. Its fixes are found through the file's daemon, which doesn't run the solution-wide
  inspections. Rider's own Fix in solution does apply them. Either apply them, or say why there was nothing, as for
  the stale results above.

- **No tool makes Rider reload a solution whose `.slnx` changed** (stall-recorder, ClaudeTooling, 2026-11-18). Three
  projects added to `ClaudeTooling.slnx` on disk didn't reach Rider's model: `inspect_code` on their folders answered
  "Nothing to inspect", and `reload_projects` refused both the new `.csproj` files ("on disk but in no project yet")
  and the `.slnx` itself ("No project found for …ClaudeTooling.slnx"). Until Magnus reloads the solution in Rider, the
  new projects get no inspections. Wanted: `reload_projects` given the `.slnx`, or a project file the solution file
  lists but Rider's model lacks, reloads the solution's project structure, as Rider's own reload of a changed solution
  file does; and the "Nothing to inspect" answer says that the files are in no project Rider has loaded.
  Here, Magnus's Reload All Projects (the solution's context menu) was enough: `inspect_code` then covered the new
  projects. But in his experience it "does not really reload everything", and the only fix he knows is restarting
  Rider, which ends every Claude session in its Claude Sessions tabs. So what a reload leaves stale is worth finding out
  when it next happens: what the MCP could reload that Rider's menu doesn't.

- **`get_file_errors` checked a stale or half-reloaded copy right after an edit on disk** (deskmancer-agent-rep,
  Deskmancer, `Deskmancer.Telemetry/Analytics/ObservedAnalyticsTevents.cs`, after Edit-tool edits of a doc comment
  only). 2026-11-17: the first call answered "{ expected" at 39:40, a position that fitted the file as it was before
  the edit, 10 lines shorter; a call a moment later answered 0 diagnostics. 2026-11-18: two calls in a row answered
  "Cannot resolve symbol 'Deliver'" (a cref) and "Cannot resolve symbol '_dispatcher'", while `dotnet build` had 0
  warnings and 0 errors; `reload_projects` on the project cleared it. The description promises that files edited on
  disk are reloaded before each call. A false error reads as a real one, which the user-level rules say to report as
  breakage, and costs a build to tell apart. Wanted: the call waits until Rider's copy holds the disk's text, and its
  model of the file is rebuilt from it, before it inspects; or it says that it couldn't.

## The rename to Royder

Magnus, 2026-11-19: Royder MCP is the preliminary name (see `CLAUDE.md`). When it is final, everything that isn't hard
to change takes it. Two things keep what they have:

- **The plugin id,** `dev.magnus.dotnet-analysis-and-refactoring-mcp`. The Marketplace knows the plugin by it: a new
  id would be a new listing, which installed copies don't update to.
- **History:** past `CHANGELOG.md` entries and commit messages.

The rest, in this order:

1. **One plugin release:** the assemblies, namespaces and project names, the development tools and specifications
   included, the Kotlin package, the README, the Marketplace display name in `plugin.xml`, and the status-bar widget.
   The host's assembly name changes with it, so it takes a restart of Rider.
2. **The plugin folder and `%LOCALAPPDATA%\DotNetAnalysisAndRefactoringMcp`,** in step with the symbol linker's
   session, whose sandbox copies both (see "What the symbol linker relies on" in `CLAUDE.md`). First check in the
   sandbox that Rider cleanly replaces a plugin whose zip has a new root folder under the same id.
3. **The `.mcp.json` key,** `dotnet-analysis-and-refactoring` → `royder`, in every project. Both keys reach the same
   URL, so it can roll out project by project. It renames every tool prefix, to `mcp__royder__find_usages` and so on,
   so permission settings, rules and skills that name the tools change with it: the tooling owner's part.
4. **The port variable,** `DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT`: a new name, no fallback, and a line in
   `CHANGELOG.md`.
5. **The GitHub repository's name,** on Magnus's word. GitHub redirects the old URLs.
6. **The clone folder, `C:\Dev\resharper-mcp`, and `ReSharperMcp.sln` with it,** when no session runs in it: this
   project's memory and session history are kept under the folder's path, and other sessions name the solution by
   `solutionName`.

## Coordinated Rider restarts

Magnus, 2026-11-19: Claude sessions will survive Rider restarts, and a tool in the tooling project, driven by the hub,
will restart Rider and put its solutions back on their virtual desktops. Agents are told why Royder is gone, and he
gets a prompt he can deny. Restarts can then happen almost any time, and an agent may request one, such as after a host
change. The design is the tooling's, in `~/.claude/dev_docs/restarting-rider.md`. It reads `/health` before a restart,
for the solutions that were open, and after, for when they are indexed again, and may reuse the development tools'
`Screen/` code for the virtual desktops. What it means for Royder, talked over with its session,
`coordinated-rider-restart`:

- **A peer takes over the primary's port slowly.** The primary's server holds port 23751, which every session calls.
  When the primary's solution closes and other solutions stay open, Royder is gone for every session until a peer's
  watchdog finds the port free and takes it over, and it checks every 30 s (`McpShellComponent.TryPromoteToPrimary`).
  The simple fix is a check every few seconds. It doesn't help a restart of Rider, which ends every backend at once.
- **Holding calls across a restart: not now.** A restart of Rider leaves no backend to hold them in: it would take a
  process outside Rider holding the port. Agents are told and call again; revisit if the failed calls turn out to cost.
- **Revisit what a restart's cost decided.** The host stays minimal (the developing-the-plugin rule) and the tools are
  swapped between calls (`dev_docs/swappable-tools.md`) because a restart was expensive. Once restarts are cheap,
  measure one, from closing a Rider until its solutions answer indexed again, and see whether each still earns its
  complexity. Swapping still saves the edit-and-test loop a restart per change, which is likely to keep it worth having.

## Headless Royder for agents' worktrees: a spike first

Magnus, 2026-11-19: "For tooling-owner and other projects to be able to fire off subagents on worktrees with full
royder support would be a fantastic boon." The aim: an agent in a worktree of its own gets the whole of Royder there,
without a Rider open on his desktop for it, taking memory and processor time. A Rider window per worktree was decided
against for that cost ("An agent Rider", under "Decided against"). The same host would give code intelligence with
Rider closed, in cloud sessions and in CI.

What is known (2026-11-19):

- **Rider's lightweight backend is no way to it.** `Rider.LightweightBackend.exe`, new in 2026.3, runs beside each
  solution's backend with 66 of the full backend's 143 zones, no `DaemonZone` and no `SweaZone`. It holds only the
  documents open in editors, which the frontend sends it, and no projects. `ZoneMarker` keeps the host out of it.
- **ReSharper's command line tools ship the platform.** The `jb` global tool (2026.3.0.1, installed here) is the whole
  engine on .NET 10, 648 MB, inspections included, with the framework InspectCode and CleanupCode are built on,
  `JetBrains.CommandLine.Products`. `jb inspectcode --eXtensions` loads extensions, but InspectCode does one job and
  exits: a server needs a host program of its own on that platform.
- **What it would save, and what not:** the window, the desktop, the frontend's work, and the lightweight backend's
  300–400 MB. Not the solution: a loaded, indexed one likely weighs what a `Rider.BackendHost.exe` does, 1–1.5 GB
  here. Unmeasured.
- **What the tools would need a second way of doing:** what goes through Rider's frontend, such as the rescans
  (`Disk/Rescans.cs`), the saves after an edit (`Disk/UnsavedDocuments.cs`) and the main-thread tools' dispatch. With
  no editors, the disk sync may get much simpler.
- **The license: no concern for our own use** (Magnus, 2026-11-19): his JetBrains subscription covers every tool they
  make. Publishing the headless mode is another matter: the command line tools are free, so it would give anyone the
  whole of Royder without a JetBrains license. Their terms get checked before it is ever published.

The tooling owner's requirements for the design (2026-11-19). Its subagents in worktrees build with `dotnet build`
today, and go without Royder:

1. **The same tools, routed by path.** A subagent calls the same `mcp__dotnet-analysis-and-refactoring` tools with a
   file in its worktree, and the call reaches that worktree's host. No second MCP server per worktree to configure: an
   agent can't add one to its own session. The routing that exists may carry it: a call that names files already goes
   to the solution holding them most closely, and a headless host could register with the primary as a peer does.
2. **Started on demand.** The first call that names a file in a worktree with no host starts one, and says it is
   indexing until it's ready, rather than failing. An explicit start as well, to warm one up before a subagent begins.
3. **Stopped when unused,** after some minutes with no call, and at once when its worktree is removed. A host must
   never outlive its folder, holding files that block `git worktree remove`.
4. **A budget.** At most a few hosts at once, at 1 to 1.5 GB each. Past it, the least recently used one stops, and a
   call that is refused says why.
5. **Separate from Magnus's Rider.** A host never writes into the solution's `.idea` folder or the user settings his
   Rider reads, so the two don't fight over the main checkout.

It offers the hub, ClaudeTooling's resident server, which already runs long-lived processes for agents such as
session hosts, as the owner of the hosts' lifecycle: ours to weigh against Royder owning it. It matters most when no
Rider is open, and so no primary to route through.

The spike, before any design:

1. A console host on the `jb` package's platform that opens one worktree's solution headless and answers
   `search_symbol` and `find_usages` over HTTP.
2. Measured on this repository and on a large solution, such as JAStudio or Compze: the time from start until indexed,
   cold and with caches, and the memory it holds.
3. A report to Magnus, with what a design would be: how agents start and stop a host, which port each gets, how a
   call reaches it, how it meets the tooling owner's requirements, and what of the tools moves behind a seam between
   the two hosts.

## Decided against

- **An agent Rider,** a Rider of its own per solution for the agents, so that a host change needs no restart of
  Magnus's (2026-11-15). He has five or six Riders open: one more for each would cost a lot of processor time and memory,
  be a hassle to handle, and break his workflow. That the agent works in the window he works in, and sees what he sees,
  is worth more.
- **Specification runs without restarting the sandbox,** when only the tools changed (2026-11-15). Measured: a run is
  67 s, and stopping and starting the sandbox, with the fresh copy's restore, about 20 s of it. It would add a second
  way to run, and weaken the check that ReSharper keeps nothing of the tools build that first ran each feature, which
  takes a fresh process.
