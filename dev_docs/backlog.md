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

- **A peer that registers on a dead peer's port, between a refused call and its removal, is removed too**
  (royder-codex-3, 2026-11-22). A refused connection removes every registration on the port (`PeerSolution.Call`),
  since nothing listens there. The window is under a millisecond, and a peer registers only once its solution has
  opened. Closing it needs an identity for each registration in the host, which "A Royder primary of its own" (under
  "The design, as decided so far") brings: remove the registrations the refused call saw, by reference, under the
  registrations' lock (royder-codex-3's `RemovePeerIfStillRegistered`, which it wrote and I held back as a host
  change).
- **Headless project membership after a deletion** (2026-11-22). The first-call bug was reproduced and fixed in Rider.
  `DiskSync` asks the frontend to rescan the containing folder when a deleted file is still in the project model,
  then waits for the item to disappear. The sandbox specifications check the first `search_symbol`, `find_usages`
  and `get_file_errors` independently, including an unresolved reference in another project.
  [Deletion specifications](../tests/DotNetAnalysisAndRefactoringMcp.Specifications/Serving/When_a_file_declaring_a_type_is_deleted_on_disk.cs)
  Rider keeps a deleted linked `Compile` item in its project. Calls that wait for its removal report the deletion
  and refuse edits if the wait expires. Later calls can edit other files without a warning about the missing item.
  [Linked file specifications](../tests/DotNetAnalysisAndRefactoringMcp.Specifications/Serving/When_a_linked_compile_item_is_deleted_on_disk.cs)
  The headless host has no frontend to service that rescan. In the spike a tracker commit removed the symbol and
  reloading the owning project removed the remaining empty project item.
  [Headless observations](../experiments/HeadlessHost/README.md#freshness-in-the-standalone-fixture)
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

- **An existing Codex session can retain an older tool list.** A session opened before the everyday cutover
  kept 26 deferred tool names after an MCP reload. HTTP `tools/list` and a fresh session showed 28. File-based
  calls from the old session still opened checkouts. This client behavior was reported to `gpt-collaborators`;
  the home tooling backlog records it in `c8c26f56`.
- **An external Git removal occasionally fails during the Windows integration fixture.** On 22 November,
  plain `git worktree remove` returned 255 with “Permission denied” for the open sibling directory in
  `royder-on-demand-mrmv80mi`. Git had forgotten the sibling by final inspection. The full repeated run
  passed both removal orders and same-path reuse. The process holding the directory at failure is unknown.

### Headless usage reports resolved on 22 November 2026

- **Two-second requests with desktop Rider closed.** The ordinary endpoint's loopback connect took 2.00 s from
  the .NET socket start event to refusal. The standalone primary now checks the OS listener table for its IPv4
  loopback endpoint on each request. It sees a desktop Rider appear on the next request. Five production samples
  after deployment gave median latencies of 12.68 ms for `/health`, 14.38 ms for `tools/list` and 12.26 ms for
  `list_solutions`; warm `get_symbol_source` on ClaudeTooling took 4.61–14.76 ms. Earlier, each was about 2.01 s.
- **Cold-start discovery.** The standalone handshake, tool description and `list_solutions` reply explain absolute
  `filePath` and full solution paths in `solutionName`. The listing continues to describe only the solutions
  actually open. Both symbol-linker consumers ignore the additional `hint` field.
- **Repeated instructions and source excerpts.** The first shortened standalone handshake was 156 characters, down from 1,852.
  Codex has been observed to repeat the guide on tool descriptions. Quota effects were not measured.
  `go_to_definition` now defaults to 200 source characters plus an ellipsis in text mode. On
  `ProcessTypingIntoASession.LoggingWhatStopsIt` the default matched explicit 200; explicit 2,000 returned the
  full 1,238 characters. The JSON declaration shape is unchanged.
- **Choosing a tool before a checkout is open.** The shared agent guidance now explains when text search and
  Royder each help. The standalone handshake explicitly covers desktop Rider being closed. Each headless analysis
  tool's `solutionName` description explains how to open the target checkout directly. An idle `/health`
  snapshot only counts projects open at that instant. Session records later that evening showed a successful
  Cloyd specification lookup by symbol and a ClaudeTooling inspection of 17 changed files.

## Finding what agents lack, from their real work (under way)

Decided with Magnus on 2026-11-23 ("That sounds a lot better to me yes"), in place of the proposed whole-task trial
of text tools against text and Royder. That trial would answer whether Royder helps overall, from one or two noisy
runs a side, and not what to change. The four-run code-reading pilot showed it: both sides scored 15.5 of 16, and
what was worth changing came from reading the runs (seven full tool descriptions printed, 16,458 characters; a whole
class fetched where one member would do). Agents keep their free choice of tools; we see what they choose, and what
that costs them. `optimize-royder`, the Claude session in this checkout, owns this since taking the work over from
`codex-optimize-royder` (earlier `codex-headless-royder`) that day.

1. **Royder's call log:** every call, its tool, its arguments' shape, its time, its answer's size, its error, whether
   it opened a checkout cold; and a command that sums it up. *Moved after the first fixes (2026-11-24):* the miner
   already sees every Royder call from the agents' side, with its answer and its time. Only cold opens and the
   primary's own failures need the log.
2. **A miner over the sessions' records,** Claude Code's transcripts and Codex's rollouts. It finds C# work in three
   kinds of episode, ranked by how often times what they cost, and samples of the costliest are read:
   - **Royder fell short:** a call failed, or was followed by a text search or read for the same thing.
   - **Royder would have done better:** an identifier searched across files and its hits read, a rename by hand, a
     whole file read for one member.
   - **No Royder:** the server was down, or the agent didn't know of it.

   *Built 2026-11-24:* `tools/agent_sessions/mine.py`. Its method, its limits and the first ranked list are in
   `dev_docs/what-agents-lack.md`. At the top: agents look up 96–100% of symbols as text, and Claude sessions rarely
   load Royder's deferred navigation tools.
3. **Fix from the top of the list:** a tool's answer, a missing capability, or the instructions and descriptions.
   First: the instructions and the loading of the navigation tools. The rule involved is in `~/.claude`, with
   `tooling-owner`. `mine.py daily` measures the effect.

   *The first fix went live on 2026-11-24, from 00:20 to 00:26 local time.* Royder's handshake instructions name the
   tool for each kind of lookup, Claude Code loads the four navigation tools at a session's start, and the
   always-loaded tool-use rule says what each C# lookup returns. `dev_docs/what-agents-lack.md` has the timeline.
   Next: read `mine.py daily` for 24–26 November against 0–4% before. If the share stays low, read the new sessions'
   lookups as text to see why. The probes' agents give a first look at that.
   Magnus passed the rule's part on to `optimize-royder` on 2026-11-24 and dropped it from his list: optimize-royder's
   change to the C# paragraph of `~/.claude/rules/030-proactive-tool-use.md` (c35aff8f), and tooling-owner's trim of
   `rules/claude-tooling/csharp-code-intelligence.md` (10bcc9bd). Judging them by this measurement is optimize-royder's.
   A change to the rules themselves still goes through `tooling-owner`.
4. **Check each fix:** probes taken from the mined episodes, run by fresh agents with free choice, before and after;
   and the mined figures over the following days. The probes stay, as a guard against regressions.

   *Built 2026-11-24:* `tools/agent_sessions/probe.py`, four probes in `probes.json`, runs in
   `dev_docs/probe-runs.jsonl`. They started after the first fix, so their first runs are its "after". Codex has no
   probe runner yet: `codex exec -s read-only --json` would give one.

Found while doing this, to fix:

- *Done 2026-11-24 (`6fc75c6`):* **a tools change reached the checkouts the primary opened only through a new primary
  package,** and switching to it waited until no managed project was open. The main declaration fix (`1be07a6`)
  waited so, in a background job of `optimize-royder`. The private Rider now serves the newest build in the everyday
  tools folder for its host, swapping between calls as the desktop Riders do, and the primary checks the host's hash
  rather than the exact build. Verified at 01:02 local time: `everyday deploy` swapped the private Rider's
  `ReSharperMcp.sln` to the new build with the desktop Riders', at its next call.
- *Done 2026-11-24 (`0204169`):* **calls that ran 60 seconds answered "The operation timed out."** That is Claude
  Code's: it gives up on an MCP call after 60 seconds (`mine.py failures`: four calls, each after exactly 60 seconds;
  no call through it ever answered after 49). The primary's cold-open wait was 60 seconds too, so its own "still
  opening" answer lost the race; and a call that ran long once the solution was open, the first symbol-wide `find_usages`
  on a worktree the primary had open, had no answer of its own at all. Two agents fell back to grep. The primary now
  answers every call within 55 seconds (`AnswerDeadline`): "still opening", or that Rider is still at the call, which
  goes on.
- *Done 2026-11-24:* **`get_symbol_source` with `allDeclarations: true` returned one overload** of
  `NoteSerializer.Serialize` in JAStudio, which has four. It returns every overload now (`Overloads`), and without
  `allDeclarations` it says how many there are.
- **A member's name stands for one of its overloads, in the tools that take a name:** name lookup keeps one
  candidate per qualified name, and overloads share theirs, so a tool works on whichever overload ReSharper lists
  first, and says nothing of the others. A parameter list in the name doesn't pick one either: `SymbolName` strips it.
  *`find_usages` covers every overload of a name since 2026-11-24 (`68e93b1`).* Next: the call hierarchy,
  `find_implementations`, `get_symbol_info` and `go_to_definition` the same way.
- *Done 2026-11-24:* **`list_symbols_in_file` lost a type's nesting after a generic method:** in Compze's
  `IThreadGate.cs`, the members after `ExecuteWithExclusiveLockWhen<TResult>` came under a "(IThreadGate)" heading.
  ReSharper models a type parameter as a type, and the outline took it for a nested type's heading. Type parameters
  are left out now, as parameters are, unless `includeLocals` asks for them.
- *Done 2026-11-24:* **a position past the end of its line answered "ReSharper failed to resolve the reference at
  56:19 (ArgumentOutOfRangeException)"** (`fix-half-deleted-worktrees`, `get_symbol_source` in `ProcessId.cs`, whose
  method had moved four lines down). The position is checked against the file first now, and the answer quotes the
  line as Rider has it.
- *Done 2026-11-24 (`52d5780`):* **73 specifications failed with "Multiple solutions are open"**, the peer sandbox's
  copy of the fixture solution still registered while other specifications ran. A peer Rider that ends without
  deregistering, crashed or killed, stayed listed until a call to its solution was refused. The server now forgets a
  peer whose port nothing listens on before it routes a request (`PeersThatStoppedListening`).
- *Done 2026-11-24 (`b6adf03`):* **two edits of one file in a row could leave Rider's copy of it at the earlier
  text,** while disk held the later one. `When_moving_the_types_of_a_file_one_after_the_other` failed so in two runs of
  four on 2026-11-24, and once on 2026-11-15: the second move answered "The file wasn't moved: Rider hadn't saved it".
  The cause, from the sandbox's logs and `tests/live_edit_race.py`, which reproduces it with renames: Rider takes in
  its own save as a change on disk (102 of 104 saves; it skipped the other two as its own), and at times its copy then
  goes back to the text before the edit (`ApplyEXTERNALDocumentChange` by `FileSystemDocumentSynchronizer`, some 30 ms
  after the save), as if it had read the file before the frontend wrote the new text; that part is inferred. The save step after
  an edit now reconciles a copy that went back while its file holds the edit's text, and logs "went back to an older
  text" each time. Left open: a copy that goes back before the save step first looks counts as saved, and stays so
  until the next call's disk sync reconciles it.
- *Done 2026-11-24:* **"Royder took most of an hour to open a worktree's solution"** (`unanswered-in-cloyd`, a
  worktree of `~/.claude`). It opened in 28 to 56 s each time, six times between 09:34 and 11:05: the private Rider
  closed after 10 idle minutes, and the agent called every 11 to 17 minutes, so nearly every call was a cold start.
  Two answered "still opening": one open ended 1 s past the 55 s deadline, and one call came 4 s into an idle close,
  waited for it, and then started a new Rider. The agent took "still opening" as a reason to fall back to grep. Now:
  30 idle minutes (`idleSeconds`), and the answer says opening takes about a minute and to retry in half a minute.
  Left open: a call that comes during an idle close waits for the close and then a cold start.
- *Done 2026-11-24 (`3225eb8`, `c89f915`, `c46c236`, primary `c46c236` installed 15:10):* **every managed worktree
  answered "still opening" for 10 to 40 minutes, then "Access is denied"** (compze-agent-rep, a JAStudio worktree,
  tessaging-and-taggregates, surrogate-session-ids). Three faults:
  - **An open waited for every sibling backend** in `RiderBackendCluster.WaitForRouting`. Two couldn't be verified: a
    worktree `git worktree remove` had unregistered while its backend held the folder, and one deleted while open. So
    each open ran out its four minutes though Rider had the solution ready in seconds. Now an open verifies its own
    backend only, and the timeout names the backends it couldn't verify.
  - **`close_worktree` refused a worktree Git no longer lists** (from tooling-owner): "Expected a configured main
    checkout or one of its linked worktrees.". Now it closes what Royder has open for the solution;
    `verify_on_demand.py --check-unregistered-close` checks it.
  - **`RiderProcessIdentity.IsAlive` threw "Access is denied"** for a reused PID now held by a process this user may not
    open: the maintenance loop's warnings all day, and after the private Rider's processes were ended, the primary
    failed to start until `current.json` of the dead run was moved aside (`current.json.dead-150546`). Now such a
    process counts as not ours.
  Learned restarting it: `Stop-ScheduledTask RoyderPrimary` ends the console wrapper and leaves `Royder.Primary.exe`
  running; end the process itself. `verify_on_demand.py` left its private Rider running after a failed run, holding
  its ports; end it before the next run. Its calls now retry "still opening", as an agent does.
- *Done 2026-11-24 (`bb98d23`):* **a rename landed at old offsets and corrupted a file** (`unanswered-in-cloyd`):
  `KindOfUnansweredItem WaitingForYourGo` became `KinSuggestionsm WaitingForYourGo`, after a script had rewritten the
  file while the private Rider was opening the solution. Rider's frontend applies the backend's edit at the backend's
  offsets to its own copy: the two copies disagreed. `tests/live_edit_after_disk_change.py` reproduced it in the
  sandbox on its first round, with a rename sent as the file is written. On a desktop Rider a 0.5 s pause was enough to
  avoid it. Now: before an edit, 100 ms for the watcher and a rescan of every suspect. After the save, a WARNING in the
  answer when the file doesn't hold the edit's result. Left open: a file the edit touches that the call doesn't name
  and the watcher didn't report, such as one changed while the private Rider was still opening, isn't rescanned. If
  the warning recurs, rescan every file the edit will change, once the tool knows them.
- *Done 2026-11-24 (`92805e0`):* **a project added to a worktree's `.slnx` left Royder with 2 of its 30 projects**
  (`surrogate-session-ids`). The private Rider reloaded the solution with the projects the edit touched only:
  `rename_symbol` refused, files were "in no project", and `reload_projects` found neither their projects nor the
  `.slnx`. Now the primary reopens a managed project whose solution file changed since it was ready;
  `verify_on_demand.py --check-solution-edit` checks it. Left open:
  - **`reload_projects` refuses a `.sln`/`.slnx`** ("No project found"). It could reload the solution, or say how.
  - **A desktop Rider takes in an edit of the solution file late:** the sandbox did nothing for over a minute, until a
    call named the file and its rescan made Rider reload. `DiskChanges`, in the host, reports source files only; the
    solution and project files could be rescanned as they change.
- *Done 2026-11-24:* **a worktree's solution answered "still opening" for about 40 minutes** (`publish-reuses-a-passed-run`,
  11:28 to past 11:50). The private Rider had it indexed, with its source generators ready, from 11:11. But the
  primary's opens failed, in its log: "Supervisor did not confirm fresh job membership after close", then "Cannot
  confirm removal is safe … 7 dotnet PIDs", each with a conhost. The job's record shows all seven as children of one
  `dotnet` a backend had started: MSBuild's reusable worker nodes, by all signs, which outlive their build by up to 15
  idle minutes. Now: the private Rider runs with `MSBUILDDISABLENODEREUSE=1` (`HostProfile.ps1`). Not understood yet:
  why an open of a solution Rider already had ready went through a close at all. The primary logs only warnings, which
  don't say. The maintenance loop failed with the same "fresh job membership" error meanwhile, under heavy load from
  three specification suites at once.
- **`When_a_file_declaring_a_type_is_deleted_on_disk.find_usages_by_name_reports_the_symbol_was_not_found` found the
  deleted type 10 seconds later,** once, in the runs before the first fix. It recurred on 2026-11-24 at 03:34, in one
  run of three: `search_symbol_by_name_finds_no_declaration` got "DrawingStencil — 1 results" right after the deletion.

The pilot's questions, answers and grading are in `~/.claude/dev_docs/usage/royder-code-understanding-trial.md`;
`codex-tooling-owner` maintains the shared usage measurements.

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
4. **Machine-wide memory decisions belong to the human or agent coordinating the machine.** Magnus revised the
   original spike request on 2026-11-22. Royder owns the lifetime of its private projects and processes. Its clients
   can request a warm-up or close a worktree. It releases idle projects automatically.
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

- **Deployed on Magnus's machine, 22 November 2026.** `RoyderPrimary` starts the standalone primary at sign-in.
  Agent clients and the symbol linker use port 23751. Ordinary Rider serves its solutions on the cluster at
  23761. The private shared Rider has a separate cluster for managed checkouts. The coordinated cutover restored
  and indexed all seven ordinary solutions. An existing agent then opened its worktree on demand through the
  original URL. [Setup, operation and verification](headless-royder/standalone-primary.md)
  - The symbol linker now waits for its first solution when the primary is cold (`a2a37a4` in ClaudeSymbolLinker).
    Its isolated sandbox still uses 23861.
- **Routing by the worktree** precedes the only-solution shortcut for calls naming files. An absolute path in a
  trusted main checkout or its linked worktree can start its project in the private Rider. Ordinary Rider takes
  precedence when it has the same checkout open.
- **Starting on demand:** one start, deduplicated by worktree and solution, from a call's first file or an explicit
  start. The call that starts a host waits for it up to about a minute, and answers that it is indexing, with the
  solution's path, past that; no edit is queued. Agents handle "retry" poorly, so most first calls should just take
  longer.
- **Stopping:** after ten idle minutes, counted from real calls, and at once when the worktree goes. A plain
  `git worktree remove` failed once the solution had loaded, on the Roslyn worker's working directory; with the
  worker's directory patched, it succeeds on its first try (royder-codex-1, below). The removal ends nothing by
  itself: an engine went on to announce its solution indexed after the worktree was gone, so the host must notice
  and stop.
- **Resource ownership.** One private Rider JVM shares its frontend across managed worktrees. Each worktree owns
  a backend and workers that can be released independently. Machine-wide scheduling belongs to the human or agent
  deciding which work should run. The recorder-based policy investigated in round 4 was superseded on 2026-11-22.
- **Files.** The private host uses the real solution, project and source paths, including for edits and MSBuild's
  `obj` files. Its IDE project stores, caches, settings, temp and logs live outside the checkout. This also covers
  trusted main checkouts, requested by Magnus on 2026-11-22 so ordinary Rider windows can be closed when he is
  not looking at the code. Only linked worktrees can be removed by `remove-worktree`.
- **The host is Rider's own headless server,** `remote-dev-server.exe`'s JVM, started directly through Java with a
  private profile, and no client ever connected (Magnus, 2026-11-22, on the recommendation from its rounds 6 to 8:
  "Sounds good. Let's do it."). Royder's plugin runs in it unchanged, the editing tools included, so the tools need no
  second implementation, and it runs under his Rider license. Its backend registers with the primary as a peer.
  - **One shared server for agents' worktrees** is the direction Magnus chose on 2026-11-22. This machine has
    repeatedly run short of memory with 96 GB installed. A separate server for this repository used about 4.3 GiB
    of private memory after indexing, including 1.8 GiB for its JVM. In a shared server, each worktree still has
    its own .NET backend and workers. On this repository, closing the second worktree released those processes
    while the first kept serving. The measurements and the remaining external-removal issue are below.
  - What it needs is in "Rider's own headless host" below: the Roslyn worker out of the worktree
    (`experiments/RemoteRoslynPatcher`), no tracked file changed in the worktree, nothing listening beyond loopback.
- **Our own program on `jb`'s engine** (`experiments/HeadlessHost`) leaves the design, decided with the host above:
  kept as an experiment for cloud sessions and CI, where no Rider is installed. With it go the four abstractions the
  tools would have chosen between per host; they come back if that host does.
- **Royder owns the hosts' lifecycle;** the hub may ask it to warm one up or stop one, as any client may.

The second round, under way:

- **royder-codex-2, done:** its program hosting Royder's unchanged host and tools, on port 23910
  (`experiments/HeadlessHost`, "Serving the unchanged Royder plugin", with `Check.py`, which repeats every case).
  - **Between calls:** an edit to a file is seen on the next call, through `DiskSync`'s reconcile. A new file, a
    deleted one and a moved one are not: the rescan's `RdCall` is unbound headless, and a new file costs the call
    `DiskSync`'s 10 s wait. `IFileSystemTracker.CommitChanges()` drops a deleted or moved file's symbols but leaves an
    empty project item; `reload_projects` on the owning project, 0.3 to 0.6 s, makes each case right. The same held
    in a project of this repository's solution, with Magnus's Rider open on it beside the host, and Rider had each
    case right afterwards (`Check.py all --paired`).
  - **The proposed headless way, unbuilt:** keep the reconcile; for files added, deleted or moved, commit the tracker,
    reload each owning project once, and check its items before answering; skip the rescan.
  - **Edit tools:** `fix_usings` works, and the engine's own transaction saves the file, before Royder would ask a
    frontend to. `rename_symbol` answers `UnsupportedLanguage` and `move_type_to_namespace` fails to initialize, both
    changing nothing: the C# refactorings' zone marker needs `ICodeEditingZone`, absent headless, so the base helpers,
    which support no language, are chosen. InspectCode's daemon logs "Unexpected transaction in command line" at an
    edit: a host that edits needs another daemon.
  - **The zones, its round 3** (`Check.py zones`): asked for through the SDK's zone activator
    (`IActivateDynamic<T>`), `ExternalSourcesZone` runs headless, and `get_symbol_source` then gives a compiled type's
    metadata view. `ICodeEditingZone` doesn't: the platform's `ZoneLicenseChecker` refuses it in the command line's
    product, so rename and move stay unsupported. The check is never to be bypassed.
  - **The daemon and the edit tools, its round 3, done** (0f84a91, `experiments/HeadlessHost`, "Editing tools on
    the fixture" and "Choosing the daemon for an editing host"). An editing host uses the platform's ordinary
    `DaemonImpl` and `DaemonThread`, chosen over InspectCode's with `IHideImplementation<T>` and no overrides: the
    same `get_diagnostics` and edited bytes as InspectCode's daemon, without its "Unexpected transaction" log, and
    with solution-wide analysis completed, which InspectCode's hadn't after 15 s. `generate_members` and
    `format_file` save real edits headless. Rename and move (refused before any change), the quick fixes (none listed,
    none applied, by either tool) and completion (no candidates: its providers need `ICodeEditingZone`) don't. No
    supported way was found to give the command line's product a license for the editing zone; nothing was signed in
    to, contacted or written. The update check is off in its launcher (`JETBRAINS_NO_CHECK_FOR_UPDATES`).
  - **The Roslyn worker's patcher, loaded by default, its round 4, done** (9ba4edd, `Check.py worktree-removal`):
    a plain `git worktree remove` of a worktree a host serves succeeded on its first try, while indexing (1.3 s,
    finishing 4 s before the host reported it indexed) and while idle after a real query (0.7 s), with the Roslyn
    worker alive across it. The host's binaries, caches and the caller must be outside the worktree.
  - **Which product can take a real license, done** (eb07e99, offline, `experiments/HeadlessHost`, "Which product can
    use a real license?"). InspectCode's licensed entity has no account, license-server or key source, and its options
    none either. ReSharper for Visual Studio's registers all three, and Rider's frontend has them in Manage Licenses;
    Rider's .NET backend has none, as its license is the frontend's. Nothing found lets the command line borrow a
    running Rider's or Toolbox's license. So the editing zone headless takes either a product that holds the license,
    such as Rider's `remote-dev-server` (royder-codex-1's round 6), or an integration JetBrains supports.
- **royder-codex-3, done:** every read tool's answer headless against Rider's
  (`experiments/HeadlessExtension/read-tool-results.md`): 18 of 30 cases the same, 10 different, 2 failed. The
  navigation, search, hierarchy, flow and file-error tools match. Missing headless: quick fixes, completion items, a
  compiled type's decompiled source (no C# metadata translator) and rename (`UnsupportedLanguage`), because
  InspectCode's composition leaves out `ICodeEditingZone` and `ExternalSourcesZone`, though their assemblies ship.
  Severities differ (UnusedMember a warning in Rider, a suggestion headless), and `changedSince` took 10 s headless
  against 0.4 s, waiting on the Rider-only rescan.
- **royder-codex-3, done:** settings parity (`experiments/HeadlessExtension/settings-results.md`, with
  `Check-Settings.ps1`, which repeats it).
  - **The severities come from Magnus's personal settings.** Rider's global settings file injects
    `%APPDATA%\JetBrains\Shared\vAny\MyResharperSettings.DotSettings`, which raises `UnusedMember.Global` and
    `MemberCanBeInternal` to warnings. This repository has no solution or project settings files.
  - **How a host should get them:** a private global settings file of its own that injects the personal file, as
    Rider's does; an injected file is mounted read-only. The experiment injected a copy. Not `--profile`: InspectCode mounts a profile above everything, so a personal indent
    overrode the repository's `.editorconfig`; injected, the repository's won, as in Rider.
  - **Not all explained:** with the personal settings, a headless `inspect_code` gave Rider's three warnings and three
    findings more, which its solution-wide analysis had stored and Rider's didn't report for that file.
  - **EditorConfig** applies the same headless and in Rider. **Line endings:** headless gives disk's, CRLF included;
    Rider turns them to LF in its documents (`RiderDocumentOperationsImplCore`).
- **royder-codex-3, round 3, done:** routing between two headless hosts, with Royder unchanged, on worktrees of one
  repository (`experiments/HeadlessRouting`, with `Check.py`, which repeats it, and `answers.txt`, the answers as an
  agent reads them).
  - **What works today:** both register and `list_solutions` tells them apart by segment; a call naming an absolute
    file reaches that worktree's host, and an edit through the primary changes the peer's file only. A relative path
    two solutions hold is refused as ambiguous, and one outside every solution is refused by the router.
  - **What the design must change:** with one solution open, every call goes to it, a file in another worktree
    included: after `DiskSync`'s 10 s wait it answers that the file is in no project, with `isError` false. A
    worktree nested in another's folder, as Claude Code's `.claude/worktrees/` are, gets the same answer from the
    outer one's host: safe here, since no project of the outer one took its files in, but wrong. Routing by the
    worktree a path is in, before the shortcut, is what the design has.
  - **A dead peer stays registered:** seen with a killed host; a replacement on the same port registered and
    answered, and a host that exits normally deregisters at once.
- **royder-codex-3, round 4, done:** the dead peer is forgotten (7aad054, deployed 2026-11-22). `PeerSolution.Call`
  recognises CoreCLR's refused connection, and removes every solution on the port; the specification registers two
  on a socket that is bound and not listening. Its first version also changed the host, to close the race now under
  "Known bugs": held back, since a host change makes every later tools build wait for a restart of all the Riders.
- **royder-codex-1, done:** the engine's footprint, on a private copy of the fixture solution
  (`experiments/HeadlessFootprint`, with the scripts that measured it).
  - **A plain `git worktree remove` succeeds only before the solution loads.** From indexing on, git deletes every
    file, forgets the worktree, and fails on the empty folder that is left (exit 255). The Roslyn worker holds it:
    `RoslynExternalProcess` sets its working directory to the solution's folder. Ending that worker alone released it.
  - **The fix, built and tested in its round 3** (`experiments/HeadlessRoslynPatcher`): a headless subclass of the
    public `CommandLineSolutionProcessStartInfoPatcher` that gives the Roslyn worker alone a private working
    directory, loaded as a package as Royder's host is. A plain `git worktree remove` then succeeded on its first
    try, during indexing and idle, with the worker running. A source generator's member, a Roslyn analyzer's warning,
    the projects and the team settings were the same as without it. The fallback it no longer needs, a private copy
    of the solution file, would have changed the solution's identity, which its settings and Royder's routing hang
    on.
  - **What it writes:** 7 files in each project's `obj`. `DisablePackageAssetsCache=true` stops the `assets.cache`
    one, with the same usages found; 18 remain. Nothing in Magnus's JetBrains folders, and existing settings files
    untouched. The caches, settings, temp and logs go where they are redirected.
  - **What outlives it:** nothing, whether the engine exits or is killed. Two loopback listeners per host, for the
    MSBuild and Roslyn workers, and nothing listening outside the machine.
- **The engine's connection to JetBrains is its update check** (royder-codex-1, 2026-11-22, round 3). About 2 s after
  start, before loading projects, `UpdatesManager.CheckForUpdatesImpl`, on a thread of its own, fetches
  `https://www.jetbrains.com/dotnet/updates/dotnet.Updates.xslt` (served through Amazon CloudFront). The request may
  carry license details, but the answer it reads is the update list: not a license check, nor feature flags or usage
  statistics. With it failing, a host indexed and answered `find_usages` as before; held unanswered, it gave up
  after 10 s while the projects went on loading. `UpdatesManager` honours `JETBRAINS_NO_CHECK_FOR_UPDATES`, which
  stopped the request: a headless host is to set it.
- **Rider's own headless host, `remote-dev-server.exe`** (royder-codex-1, round 5, read only,
  `dev_docs/headless-royder/rider-remote-dev-server.md`). A licensed route to the whole of Royder, editing included,
  with Royder's plugin unchanged, if a host with no client allows the editing zone. Its `run` opens a project with no
  client connected. Its license is checked on the client at connection, so what a never-connected host allows takes a
  run to see. Its starter can hold several projects in one JVM; whether each solution gets a backend of its own, and so
  what a worktree costs, takes a run too. Magnus, 2026-11-22, on whether a trial may use his license: "Of course you
  can use my license."
  - **Its round 6, done** (89f2dc6, "Round 6" in the same document): **a host that no client ever connected to runs
    Royder with the editing zone.** Both backends ran `ICodeEditingZone`, and a dry-run `rename_symbol` gave the right
    change. It ran on this machine, isolated as the sandbox Rider is, with Royder its own primary on 24941. A second
    solution joined the same JVM with a full backend of its own, as a peer of the first. Memory: 3.3 GiB for the
    first small solution, the JVM's 1.8 GiB included, and 0.66 GiB more for the second. One `stop` ended both. Not
    contained by the four IDE folder overrides: status files in the real `%LOCALAPPDATA%\JetBrains\RemoteDev`, which
    it moved out, and touched timestamps on `PermanentUserId` and `PermanentDeviceId`, which it restored.
  - **royder-codex-1's round 7, done** (8e08c5a, "Round 7" in the same document): the same host serving a worktree
    of this repository. Every editing tool worked as in Rider:
    - a committed rename across two files;
    - a namespace move with its file;
    - a quick fix and suggestions;
    - completion, with the same 134 items;
    - project reload, and the disk sync of an edit and of new files.
    The exceptions:
    - **A deleted file lingered** 12 s or more. Its build predated 4a3cd7f.
    - **A plain `git worktree remove` failed** on the Roslyn worker's working directory, and the host wrote
      `.idea` files back into the emptied tree.
    - **Memory:** 4.4 GiB for the host once indexed, its JVM 2.1 GiB of that, in 21 s cold.
  - **royder-codex-3's round 8, done** (a829dbf, "Round 8" in `remote-dev-server-lifecycle.md`):
    - **Starting Java directly keeps the host's profile private.** The argument file is built from
      `product-info.json`'s Windows entry, with private `APPDATA`, `LOCALAPPDATA` and the four `idea.*.path` folders.
      The status files and both ID files were written only there, and nothing in Magnus's profile changed.
    - **A deleted file is gone at the first call,** by name, in all four trials, with the build of 4a3cd7f. The JVM
      received Royder's rescan of its folder during that call.
    - **The heap cap barely matters:** at `-Xmx768m` and `-Xmx1024m`, 4.26 and 4.28 GiB private for the whole host,
      the JVM 1.8 GiB in both, with no full collection. About 1 GiB of the JVM is not heap.
    - **What outlives the JVM:** a private `jetbrainsd.exe`, ended by the job. The host also changed the worktree's
      tracked `.idea/.idea.ReSharperMcp/.idea/vcs.xml`.
  - **royder-codex-2's round 6, read-only, done** (9cfc8f5,
    `dev_docs/headless-royder/remote-dev-server-closing-one-project.md`):
    - **No stock request lets a .NET backend close its own project.** `Solution.CloseRequested` goes from the JVM to
      the backend.
    - **The route:** a JVM `RestService` on IntelliJ's built-in HTTP server, called by Royder's primary with the
      solution's absolute path and a per-host token, which calls `ProjectManager.closeAndDispose`. Closing a project
      releases its backend's protocol, which should end that backend alone: inferred from the code, not run.
    - **Opening another solution in a running server is supported:** `run` with the same config and system folders
      hands the path to the running JVM, which opens it in a new frame.
  - **royder-codex-1's round 8, done** (ad80651, `experiments/RemoteRoslynPatcher`, "Round 8" in
    `rider-remote-dev-server.md`): the patcher, as a plugin of its own for agents' servers, built and not run. `.idea`
    is beside the solution, in the worktree, with no setting to move it, and is written on project save and close.
  - **The shared-server experiment:**
    - **royder-codex-3's launch harness is on main** (4239f08, `experiments/RemoteDevHost`). The private JVM keeps
      the tracked `vcs.xml` intact with `vcs.root.auto.add=false`. The supervisor ends the private
      `jetbrainsd.exe` that waits for another IDE after Java exits. A host in the smoke test returned the right
      declaration from its own checkout after six hours. Shutdown left no job members or reserved listeners.
    - **royder-codex-2's round 7, done** (7d2b496, `experiments/RemoteCloseWorktree`): its close request, built as a
      plugin of its own for agents' servers, not in Royder's jar, which every Rider of Magnus's would load. The Plugin
      Verifier found it compatible. On RD-263.7307.325 the primary's authenticated loopback request with neither
      Origin nor Referer passes the trust check without a dialog. The scheduled close receives the required write
      intent. Both findings came from the installed code.
    - **royder-codex-1 measured two real worktrees in one JVM** with both plugins. The first used 4.02 GiB
      private. Opening its sibling brought the total to 6.25 GiB, then 5.82 GiB on reopening it. Closing the
      sibling brought the total back to 4.05 and 4.02 GiB. File-path requests found the right worktree and the
      first kept serving after both closes. Both plain Git removals succeeded on the first attempt. [Round 9 measurements](headless-royder/rider-remote-dev-server.md#round-9--two-worktrees-in-one-rider-jvm-22-november-2026)
    - **First-call readiness:** the reopened backend reported `indexing=false` while a source-generated symbol
      still failed to resolve. The Roslyn-dependent probes passed 6.5–7.9 seconds after the first probe on two
      openings. On-demand startup needs a readiness check that covers those calls.
    - **Managed removal should close the project before Git removes its worktree.** A third cycle tested the
      reverse order. Git removed the still-open sibling, with Roslyn running. Rider recreated
      `projectSettingsUpdater.xml` and `workspace.xml` under `.idea` on project close. External removal still
      needs a way to prevent or safely clear those writes, including when another checkout has reused the path.
    - **Two network-access prompts from Windows' firewall** (Magnus, 2026-11-22): waiting when he came back to the
      machine after a night of these runs. The Windows Firewall log identifies the two programs he allowed at 08:19
      as `remote-dev-server.exe` and Rider's bundled `java.exe`. Which sockets prompted the approvals remains open.
      The agent server's listeners should stay on loopback.
    - **Later:** measure what disabling unused bundled plugins saves in the JVM.
  - **royder-codex-3's round 7, read-only, done** (0cc8a05,
    `dev_docs/headless-royder/remote-dev-server-lifecycle.md`):
    - **The status files can't be redirected with the stock launcher:** it resets `APPDATA` and `LOCALAPPDATA` to
      the real folders before starting the JVM. The JVM's ID files are always under `APPDATA\JetBrains`, and are
      rewritten unchanged. Untested candidate: starting Java directly, as `rider.bat` does, with private folders.
    - **`stop` ends the whole JVM,** whatever solution it names. Only a connected client can close one project. So
      a host per worktree, in a JVM of its own, is what stops one worktree's host alone.
    - **No idle exit** for a host no client connected to: the primary has to stop it.
    - **Several JVMs at once:** distinct config and system folders and `--port` values.
    - **Royder's rescans and saves have their handlers in the host's JVM**
      (`FileSystemModelProtocolListener`, `DocumentHostHelper.saveFiles`), as in the desktop frontend.
- **To merge: royder-codex-3's "Release a rejected worktree after verifying its absence"** (52bd04e, 2026-11-22),
  never merged into main. It is on the branch `royder-codex-3-rejected-open`, pushed to origin on 2026-11-24 when
  `worktree-cleanup` removed the worktree. The commit records open attempts in their private Rider generation. When
  the JVM reports the solution absent and a fresh job sample accounts for every backend and worker, cleanup succeeds
  without dispatching a close; `verify_on_demand.py --check-rejected-open` exercises it. de5f76b, on a lost open
  reply, is a different case. The merge into main conflicts in `SharedRiderHost.cs` and `verify_on_demand.py`. Its
  Codex session is archived: `ClaudeTooling sessions restore codex:01a200ba-ae8a-7a03-8129-80982bcaf875`.

## Reading dotTrace snapshots without desktop Rider

Asked by testrun-performance, 2026-11-23: it needed a sampling snapshot's (`.dtp`) call tree with Rider closed after a
reboot, and fell back to dotTrace's `Reporter.exe`, which gives each function's total and own time and callers but no
tree to drill into and no filters by thread or time. Rider's dotTrace tools (`dotTraceGetCallTree`,
`GetSnapshotInfo`, `GetSnapshotFilters`, `GetTimeline`, `GetTimelineEvents`) are `DotTraceMCPToolset` in Rider's bundled
`dotTrace.dotMemory` plugin (`lib/modules/intellij.rider.plugins.dotTrace.dotMemory.mcp.jar`), served by JetBrains'
built-in MCP server, with dotTrace's engine (`JetBrains.DotTrace.Dal` and others in the plugin's `DotFiles`) in the .NET
backend.

- **The route, decided:** forward those tools to the private Rider the primary already runs, which disables no plugins.
  A spike first: does the private Rider load the dotTrace plugin, and can its built-in MCP server run there? About a
  day with specifications if both hold. It starts the private Rider when none runs: about 4 GB, some tens of seconds.
- **If the MCP server can't run headless:** Royder's tools in that backend call dotTrace's components themselves.
  Internal API, days of work, at risk at each Rider release.
- **Not:** reading snapshots in the primary's own process through `JetBrains.DotTrace.Dal`, which needs JetBrains'
  component container.

## Decided against

- **An agent Rider,** a Rider of its own per solution for the agents, so that a host change needs no restart of
  Magnus's (2026-11-15). He has five or six Riders open: one more for each would cost a lot of processor time and memory,
  be a hassle to handle, and break his workflow. That the agent works in the window he works in, and sees what he sees,
  is worth more.
- **Specification runs without restarting the sandbox,** when only the tools changed (2026-11-15). Measured: a run is
  67 s, and stopping and starting the sandbox, with the fresh copy's restore, about 20 s of it. It would add a second
  way to run, and weaken the check that ReSharper keeps nothing of the tools build that first ran each feature, which
  takes a fresh process.