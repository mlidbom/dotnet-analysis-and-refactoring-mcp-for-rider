# Backlog: what the plugin should do better

What Claude found the plugin lacking, or would build next, most valuable first. Sessions in other projects add what
they run into here (see the user-level rule that the dotnet-analysis-and-refactoring MCP is a development tool you own). An item that is done goes from the list.

## First: every tool's answer, as an agent reads it

Magnus, 2026-11-20: first in the backlog. `get_symbol_source` and `get_call_hierarchy` answer in pretty-printed JSON
where text would serve an agent better: source in an escaped string, each file's full path repeated at every node, an
empty `"children": []` at every leaf. That costs tokens and makes the answer harder to read. Other tools may do the
same; only these two were checked. Wanted: go through every tool's answer as an agent reads it, and for each that
answers in JSON where text would serve better, a text form, with the JSON on `format: "json"` where a program reads it.
The JSON the symbol linker reads stays as agreed (see "What the symbol linker relies on" in `CLAUDE.md`). The text
forms in the next section are part of this pass.

## Next: reading code without opening files

Magnus's ideas, 2026-11-20, as Claude judged them against what the tools answered that day. After the pass over the
tools' answers, and above the known bugs: nearly every task in which an agent reads code would use it, where each bug
bites now and then. In the order to build them:

1. **`get_call_hierarchy`'s outgoing tree, which the new tool builds on:** it follows only calls through a name, so not
   `new`, property accessors or operators, and stops at an interface or virtual member, without its implementations. It
   lists every compiled callee, such as `List.Add` and `StringBuilder.Append`, and repeats each file's full path at
   every node: for `GetSymbolSourceTool.Execute` at depth 2, 48 nodes, 16 of them the solution's, in 16,912 characters
   of JSON. Wanted: implementations followed, an indented text outline of `Type.Member  path:line`, JSON on
   `format: "json"`, and the scope below.
2. **`get_reachable_source`, the reachable source of a method, in one call:** the source of a method and of every
   method in the scope below that its calls can reach, to a depth, each once, after an outline of the call tree. A call
   through an interface or a virtual member reaches every implementation and override in the scope, up to a cap,
   beyond which they are named and counted. A budget in lines of source, and the answer says where it stopped: at the
   depth, at the budget, or at a call that can't be followed without running the code, such as a delegate's. An agent
   does this today with a `get_symbol_source` or a file read for each callee, and a `find_implementations` for each
   interface call. `flow` inlines callees, but paraphrased, and follows a virtual call only when it has a single
   implementation. A tool of its own, not an argument to `get_symbol_source`: its answer is a tree with a budget and
   where it stopped, not one symbol's source.
3. **`get_call_hierarchy`'s incoming tree:** the same text outline, with each call site's line of code, as `find_usages`
   shows it, so the agent sees what is passed without opening the file.
4. **`get_symbol_source`'s answer:** the source is a JSON string, every newline and quote escaped, without line numbers.
   Wanted: plain text with line numbers, as a file read shows it, under a `Type.Member — path:start-end` line.

**The scope a call tree follows,** for the outgoing tree and `get_reachable_source`: by default the solution's own
code, which no configuration needs and which removes the noise measured above. Arguments narrow it to namespaces, or
widen it to named compiled assemblies, such as Compze's. Not a setting kept for the session (Magnus asked, 2026-11-20):
the server keeps no state per client, its one `Mcp-Session-Id` is the process's, and a call that answers by what an
earlier call set is one an agent misreads once that call is compacted away. A project's defaults could come later from
a header in its `.mcp.json`, which Claude Code sends with every request, if the arguments are found to be repeated.

## Known bugs

- **Suspected in Rider, seen headless: a deleted file may not get the rescan** (royder-codex-1's reading of the source,
  2026-11-22). `DiskChanges` records a delete, but `DiskSync.Check` skips the paths that no longer exist, so a deleted
  path doesn't itself ask the frontend to rescan. Headless, with no frontend, a deleted file's type was still found
  12 s later (royder-codex-2). In Rider, its own project-model tracking may remove it anyway. To check: delete a file
  declaring a type, then `search_symbol` the type by name alone.
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

- **A wide namespace rename broke code without saying so, and took one call per type** (tooling owner, renaming
  `ClaudeTooling.ResidentServer` to `ClaudeTooling.Hub` in `~/.claude`'s ClaudeTooling solution, 2026-11-19/20; the
  incident is in `~/.claude`'s incidents, commit a5702e0). What would have helped most: a namespace rename that checks
  that every reference it rewrites still binds. Four failures:
  - `rename_symbol` refuses a namespace, answering "UnsupportedLanguage". The rename took 84 `move_type_to_namespace`
    calls instead of one.
  - `move_type_to_namespace` threw an `ArgumentOutOfRangeException` on a partial class with a `[LibraryImport]` method
    (`StartingTheServer`). Unverified guess: the source generator's part of the class, in `obj/`.
  - One move shortened a qualification to `Hub.ActivityOfTheServer`, and a later move broke it: the next bullet.
  - `rename_symbol` renamed a partial class's first file with its type, `WarningsOfAspNetCoreInTheServersLog.cs`, but
    left its second file's name, `WarningsOfAspNetCoreInTheServersLog.Logger.cs`; the agent renamed it by hand. The
    file-per-type standard names a partial's files `Type.Part.cs`, so a rename should rename them all.

- **`move_type_to_namespace` breaks a reference that an earlier move left compiling, and says nothing** (two cases, in
  `~/.claude`'s ClaudeTooling solution). Each move compiled; a later one broke it with CS0234, and neither reported a
  problem.
  - The tooling owner's rename of `ClaudeTooling.ResidentServer` to `ClaudeTooling.Hub` (2026-11-19/20): one move
    shortened a qualification to `Hub.ActivityOfTheServer`, and a later move broke it, without rewriting that
    reference. `get_file_errors` had reported no errors after the first move.
  - gpt-collaborators' split of `ClaudesSetupForCodex.McpServers` (2026-11-21, `~/.claude` d8a1a02), the smallest
    repro yet: `ClaudesMcpServersFile` uses `ClaudesMcpServer`, both in `McpServers`. Moving `ClaudesMcpServer` to
    `McpServers.ClaudesDefinitions`, then `ClaudesMcpServersFile` after it, left `McpServers.ClaudesMcpServer` in three
    places in `ClaudesMcpServersFile.cs` (two return types and a `new`), naming the namespace the type had left with
    the first move. It also left the `using ClaudesSetupForCodex.McpServers.ClaudesDefinitions;` the first move had
    added, now the file's own namespace. Unverified guess: the second move qualifies what the moved type used from its
    old namespace by where its model last saw it, before the first move.

  Wanted: a move that leaves the solution compiling, checked over every reference to the namespaces it changes, not
  only those it rewrites, as the first case needs; and no using left that names the file's own namespace. Minor, from
  the same split: moving `WrittenMcpServers` added a using to `IWrittenForCodex.cs` only for a cref to it, where the
  file qualifies its other crefs by namespace (`Skills.LinkedSkills`). A cref rewritten as its file writes the others
  would fit. Not checked: whether that is ReSharper's own Move refactoring following its reference-qualification
  settings.

- **`rename_symbol` of a nested type leaves its file's name** (gpt-collaborators' refactoring subagent, 2026-11-21
  ~16:55, in `~/.claude`). `Peers.Codex.CodexSessions.ReadFromCodex`, nested in the partial class `CodexSessions`, in
  `src/Peers/Codex/CodexSessions.ReadFromCodex.cs`, renamed to `CodexOfTheHome`: the type and its references changed,
  the file kept its name. Its description says "a type's file named after it, which is renamed with it", which holds
  for a top-level type. Wanted: a nested type's file, `Outer.Nested.cs` by the file-per-type standard, renamed to
  `Outer.NewName.cs`; and the outer type's rename renaming each `Outer.*.cs` with it, as in the bullet above. The
  subagent renamed it with `git mv` (`~/.claude` cef7332).

## MCP tool annotations, for Codex

Codex has Royder since 2026-11-21 (gpt-collaborators, `~/.claude` c794756), and it is a second client of the same
server. None of the 26 tools carries MCP annotations, `readOnlyHint` included. Codex's default treats a tool not
marked read-only as one needing approval, so every Royder call goes through its approvals reviewer, a model call of
its own; that day the reviewer let each through without asking Magnus. Wanted: `readOnlyHint: true` on the tools that
only read (`search_symbol`, `find_usages`, `get_diagnostics` and the like), so Codex skips the review for them; the
edit tools left unmarked, or marked as the MCP specification's other hints fit. Not checked: what Claude Code does
with the annotations.

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
4. **A budget of memory, not of hosts,** at 1 to 1.5 GB each. Magnus, 2026-11-19: "Check my machine's memory status
   via the service provided by the stall monitor. If there is plenty and has been for a while. Don't block." The
   machine stall recorder's live samples (`MachineStalls.LiveSamples`, a named pipe that hands a viewer the last five
   minutes of samples, then each as it is taken; see `~/.claude/dev_docs/machine-stall-recorder.md`) say whether
   memory has been plentiful for a while, by the recorder's own thresholds (`WarningThresholds.Default`: the memory
   gauge turns yellow at 85% in use, about 14 GB free of 94). Then a host starts, however many run. Only when memory is
   short does the least recently used one stop first, or the call is refused and says why. Memory did run out on
   2026-11-18, twice: about 0 GB available, and the commit charge at its limit, from 08:00 to 09:00 and 19:00 to 21:00.
   Open: what decides where no recorder runs, in cloud sessions and in CI. The tooling owner keeps the pipe and the
   thresholds as a contract with Royder (`~/.claude` commit `fefc2af`): they change only by adding, and Royder's
   session is told first. Once Royder reads them, tell it which of the samples' fields Royder relies on, so it names
   them there.
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

The spike's first round, 2026-11-22, by the Codex helpers (`dev_docs/codex-helpers.md`), each on an independent
question, as gpt-collaborators advised; `headless-royder` coordinates, reviews and merges. Each experiment's README says
how to run it and what it found:

- **Our own program boots the engine headless** (`experiments/HeadlessHost`, royder-codex-2). It calls
  `CommandLineProductMain.Main` with `isLiveSession: true`, owns the dispatcher and the solution's lifetime, and its
  find-usages answer matched Rider's. A second query in the same process: 34 ms. Loading and indexing, cold and
  warm, on a quiet machine (about 40% processor use): this repository 13 s and 11 s; a copy of Compze.AllProjects
  (125 projects) 40 to 46 s cold, at 72 to 83% processor use, and 28 s warm. On a busy machine, near 90%, warm took
  19 s and 49 s. Memory once indexed: the engine 0.7 to 1.2 GB private; with the MSBuild and .NET workers it starts while
  loading projects, 1.27 GB on Compze, so a budget counts the process tree. An edit on disk is seen only after
  `IFileSystemTracker.CommitChanges()`, even with the tracker enabled. Loading projects with `--no-build` still writes
  `obj\**\*.assets.cache` in the checkout, so a host only loads its own worktree.
- **Royder's unchanged host loads in `jb`'s engine** (`experiments/HeadlessExtension`, royder-codex-3): as an
  InspectCode extension from a local feed, on .NET 10 and 11, with its components constructed and
  `list_symbols_in_file` answering. An extension can't add a `jb` command of its own, and in an InspectCode run file
  tracking is off, so the route stays a reader: our own program is the host for editable worktrees.
- **What only Rider gives** (`dev_docs/headless-royder/rider-dependencies.md`, royder-codex-1): every ReSharper type
  Royder uses ships in `jb`. Rider alone gives the frontend's rescans and saves, its document operations and project
  reload executor, and the text controls `complete_at` and `apply_quick_fix` use; `jb` has a candidate for each.

**The design, as decided so far** (headless-royder, 2026-11-22, on royder-codex-1's proposal,
`dev_docs/headless-royder/design-proposal.md`, which gives the alternatives and their tradeoffs):

- **A Royder primary of its own** holds the MCP port, 23751, whether or not a Rider runs, and needs no ReSharper:
  it routes, and lists the tools from a manifest of the released tools when no solution is open. Riders and
  headless hosts all register with it as peers, on ports of their own. It also keeps Royder up across a Rider
  restart, which the coordinated restarts above lack. Started at logon, as the stall recorder is: a new scheduled
  task on Magnus's machine, to be put to him before it is installed. The Riders' hosts stop competing for the port,
  a host change: batched, for a restart.
- **Routing by the worktree** a call's absolute paths are in, before the shortcut to the only open solution.
  Registrations get an identity of their own, not only the solution's path.
- **Starting on demand:** one start, deduplicated by worktree and solution, from a call's first file or an explicit
  start. The call that starts a host waits for it up to about a minute, and answers that it is indexing, with the
  solution's path, past that; no edit is queued. Agents handle "retry" poorly, so most first calls should just take
  longer.
- **Stopping:** after ten idle minutes, counted from real calls, and at once when the worktree goes. A plain
  `git worktree remove` fails once the solution has loaded, on the Roslyn worker's working directory (royder-codex-1's
  footprint, below): the fix is to be built and tested before anything is built on stopping.
- **Memory:** the primary reads the stall recorder's pipe all the time, the fields `at`, `availableMB`, `physicalMB`,
  `committedGB` and `commitLimitGB` (told to the tooling owner once read), and admits a start while memory has been
  green for 30 s, reserving the starts in progress, counted as process trees. When it's short, the least recently
  used idle headless host stops first. Riders are never stopped.
- **Files:** each host's caches, settings, temp and logs are private, outside the worktree. A host loads only an agent's
  own worktree, whose `obj` it may write: not the main checkout, nor any solution a Rider has open.
- **One tools build for both hosts,** with what differs behind four abstractions chosen per call by which host
  serves the solution: bringing the model up to date with disk, saving an edit's documents, reloading projects, and
  text controls. The host DLL is the same bytes in both, so one `host.sha256` serves both.
- **Royder owns the hosts' lifecycle;** the hub may ask it to warm one up or stop one, as any client may.
- **Our own program** hosts the engine, with `isLiveSession: true`. The extension route stays a compatibility check.

The second round, under way:

- **royder-codex-2, done:** its program hosting Royder's unchanged host and tools, on port 23910
  (`experiments/HeadlessHost`, "Serving the unchanged Royder plugin", with `Check.py`, which repeats every case).
  - **Between calls:** an edit to a file is seen on the next call, through `DiskSync`'s reconcile. A new file, a
    deleted one and a moved one are not: the rescan's `RdCall` is unbound headless, and a new file costs the call
    `DiskSync`'s 10 s wait. `IFileSystemTracker.CommitChanges()` drops a deleted or moved file's symbols but leaves an
    empty project item; `reload_projects` on the owning project, 0.3 to 0.6 s, makes each case right.
  - **The proposed headless way, unbuilt:** keep the reconcile; for files added, deleted or moved, commit the tracker,
    reload each owning project once, and check its items before answering; skip the rescan.
  - **Edit tools:** `fix_usings` works, and the engine's own transaction saves the file, before Royder would ask a
    frontend to. `rename_symbol` answers `UnsupportedLanguage` and `move_type_to_namespace` fails to initialize, both
    changing nothing: the C# refactorings' zone marker needs `ICodeEditingZone`, absent headless, so the base helpers,
    which support no language, are chosen. InspectCode's daemon logs "Unexpected transaction in command line" at an
    edit: a host that edits needs another daemon.
- **royder-codex-3, done:** every read tool's answer headless against Rider's
  (`experiments/HeadlessExtension/read-tool-results.md`): 18 of 30 cases the same, 10 different, 2 failed. The
  navigation, search, hierarchy, flow and file-error tools match. Missing headless: quick fixes, completion items, a
  compiled type's decompiled source (no C# metadata translator) and rename (`UnsupportedLanguage`), because
  InspectCode's composition leaves out `ICodeEditingZone` and `ExternalSourcesZone`, though their assemblies ship.
  Severities differ (UnusedMember a warning in Rider, a suggestion headless), and `changedSince` took 10 s headless
  against 0.4 s, waiting on the Rider-only rescan.
- **royder-codex-3, next:** settings parity: which of Rider's layers make the difference, whether a headless host can
  read them read-only, a positive EditorConfig check, and which side's line endings are faithful to disk.
- **royder-codex-1, done:** the engine's footprint, on a private copy of the fixture solution
  (`experiments/HeadlessFootprint`, with the scripts that measured it).
  - **A plain `git worktree remove` succeeds only before the solution loads.** From indexing on, git deletes every
    file, forgets the worktree, and fails on the empty folder that is left (exit 255). The Roslyn worker holds it:
    `RoslynExternalProcess` sets its working directory to the solution's folder. Ending that worker alone released it.
  - **The fix it recommends, unbuilt:** a headless subclass of the public `CommandLineSolutionProcessStartInfoPatcher`
    that gives the Roslyn worker alone a private working directory. To be tested with the worker running, with a
    source generator's member, an analyzer's diagnostic and the team settings still there. The fallback, a private
    copy of the solution file, changes the solution's identity, which its settings and Royder's routing hang on.
  - **What it writes:** 7 files in each project's `obj`. `DisablePackageAssetsCache=true` stops the `assets.cache`
    one, with the same usages found; 18 remain. Nothing in Magnus's JetBrains folders, and existing settings files
    untouched. The caches, settings, temp and logs go where they are redirected.
  - **What outlives it:** nothing, whether the engine exits or is killed. Two loopback listeners per host, for the
    MSBuild and Roslyn workers, and nothing listening outside the machine.
- **The engine's connection to JetBrains is its update check** (royder-codex-1, 2026-11-22, round 3). About 2 s after
  start, before loading projects, `UpdatesManager.CheckForUpdatesImpl`, on a thread of its own, fetches
  `https://www.jetbrains.com/dotnet/updates/dotnet.Updates.xslt` (served through Amazon CloudFront). The request may
  carry license details, but the answer it reads is the update list: not a license check, nor feature flags or usage
  statistics. With it failing, or held unanswered, a host indexed and answered `find_usages` as before: nothing waits
  on it. `UpdatesManager` honours
  `JETBRAINS_NO_CHECK_FOR_UPDATES`, which a headless host is to set.

## Decided against

- **An agent Rider,** a Rider of its own per solution for the agents, so that a host change needs no restart of
  Magnus's (2026-11-15). He has five or six Riders open: one more for each would cost a lot of processor time and memory,
  be a hassle to handle, and break his workflow. That the agent works in the window he works in, and sees what he sees,
  is worth more.
- **Specification runs without restarting the sandbox,** when only the tools changed (2026-11-15). Measured: a run is
  67 s, and stopping and starting the sandbox, with the fresh copy's restore, about 20 s of it. It would add a second
  way to run, and weaken the check that ReSharper keeps nothing of the tools build that first ran each feature, which
  takes a fresh process.
