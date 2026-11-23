# What agents lack: mined from their real C# work

The plan is in the backlog, under "Finding what agents lack, from their real work". This document holds the miner's
method, its limits, and what it found.

## The miner

`tools/agent_sessions/` reads every Claude Code transcript and Codex rollout on this machine into one shape: sessions,
and their tool calls in order. It then works out what each call did with C# source, and finds the episodes in which
Royder fell short or would likely have done better.

~~~bash
python tools/agent_sessions/mine.py rank        # the kinds of episode, ranked by cost
python tools/agent_sessions/mine.py breakdown   # all C# work, by operation
python tools/agent_sessions/mine.py daily       # symbols looked up as text against through Royder, per day
python tools/agent_sessions/mine.py samples symbol-searched-as-text --top 10
python tools/agent_sessions/mine.py royder      # Royder's calls by tool
python tools/agent_sessions/mine.py sessions    # C# sessions: Royder offered, used, and their cost
~~~

Each command takes `--since` (default 2026-11-16), `--until` and `--agent claude|codex`. A run takes about 15 seconds.

**Cost** is tokens of context read because of a call. A call costs its share of the context that the response making
it read, plus its output, which every later response reads again until a compaction. It measures how heavy the C#
work was, not what Royder would save: a Royder answer has a cost of its own.

**Kinds of episode,** in the order calls are assigned to them (`episodes.KINDS`):

- `royder-answered-nothing`: a Royder call failed or found nothing, and the C# searches and reads after it.
- `royder-then-searched-as-text`: a text search for a name Royder had just been asked about.
- `declaration-searched-as-text`, `symbol-searched-as-text`: a text search whose alternatives are mostly symbol
  names (`Foo`, `class Foo`, `static bool IsFoo`, `\.Foo\(`), and the reads of its hits within the next six calls.
- `renamed-by-hand`: the same identifier swap in two or more edits, or in a shell replacement.
- `whole-file-read`: a whole C# file of more than 8,000 characters.
- `compile-errors-found-by-building`: a build or test run that stopped on `error CS`.

"Then edited" says whether a file the episode read or found was edited within the next 20 calls. Reading a file
before editing it is necessary anyway, and Claude Code's Edit requires a Read first.

**Limits:**

- In two random samples of 30 and 25 symbol lookups, about four in five were real symbol lookups. The rest were
  text searches that happen to name identifiers.
- A Codex script's calls share its output, and its cost, equally.
- For Claude subagents, whether Royder was offered isn't recorded. They inherit their parent's tools.
- Shell commands are parsed by heuristics: segments split at `&&`, `||`, `;`, pipes and line breaks outside quotes,
  with here-documents removed.

## First findings, 2026-11-24 (sessions from 16 to 23 November)

224 sessions did C# work: 23,982 calls costing 7.8G tokens. Royder's 1,344 calls cost 0.33G of that.

| kind | episodes | sessions | cost | of the C# work's | then edited |
|---|---|---|---|---|---|
| symbol-searched-as-text | 3,616 | 193 | 2,037M | 26.1% | 17% |
| declaration-searched-as-text | 1,180 | 153 | 699M | 9.0% | 14% |
| whole-file-read | 613 | 129 | 367M | 4.7% | 13% |
| compile-errors-found-by-building | 274 | 56 | 93M | 1.2% | 1% |
| renamed-by-hand | 74 | 35 | 34M | 0.4% | 7% |
| royder-answered-nothing | 50 | 25 | 14M | 0.2% | 2% |
| royder-then-searched-as-text | 9 | 8 | 7M | 0.1% | 33% |

### 1. Agents don't use Royder to navigate

Royder's navigation tools are `find_usages`, `go_to_definition`, `get_symbol_source`, `get_symbol_info`,
`search_symbol`, `list_symbols_in_file`, `find_implementations` and the hierarchies. They had 117 calls. Over the
same days, agents looked symbols up as text about 4,800 times. Each day, 0 to 4% of lookups went through Royder. That
didn't change on 22 and 23 November, when the standalone primary began opening checkouts on demand. Agents use Royder
to check and refactor: `inspect_code` 359 calls, `get_diagnostics` 343, `rename_symbol` 191, `move_type_to_namespace`
141.

Only 14–17% of these lookups led to an edit of a file they found within 20 calls. Most were made to understand code,
which is what the navigation tools are for. When agents did use them, they rarely fell back to text: 9 episodes of
`royder-then-searched-as-text` against 117 navigation calls.

What keeps agents from these tools, as far as the records show:

- **In Claude Code, Royder's tools are deferred.** A session must load a tool's schema through ToolSearch before
  calling it, and loads only the tools it has already decided to use. Of 186 sessions that were offered Royder, 29
  ever loaded a navigation tool. The ToolSearch queries loaded `inspect_code` 80 times, `get_diagnostics` 61 and
  `find_usages` 21.
- **The rule that presents the navigation tools is path-scoped.** `~/.claude/rules/claude-tooling/csharp-code-intelligence.md`
  loads only when the Read tool opens a matching file. It reached 98 of the 205 transcripts that touched C#. Agents
  that search and read through the shell never get it. When it does load, Royder's navigation gets one sentence. The
  rest is about JetBrains' Rider MCP, SCSS and Sherlock.
- **The always-loaded instructions don't say to prefer Royder.** "Choose the tool that fits the question" leaves the
  choice to habit, and habit is grep. This applies to `~/.claude/rules/030-proactive-tool-use.md` and to the
  matching paragraph of `~/.codex/AGENTS.md`.

The records can't show whether Royder's answers would serve every lookup's need. The probes in step 4 of the plan
test that.

### 2. Whole files read

613 reads of large C# files, 13% of them followed by an edit. The alternative is `list_symbols_in_file`, then
`get_symbol_source` for the members needed. This sits under the same instructions gap as finding 1.

### 3. Builds that stopped on compile errors

274 builds and test runs in 56 sessions. `get_file_errors` after an edit finds compile errors in seconds.

### 4. Renames by hand, mostly of namespaces

74 episodes. Many of them replace namespace segments with `sed` or edits, such as `hub -> localapi` and
`residentserver -> localapi`. `rename_symbol` refuses a namespace ("cannot be renamed: UnsupportedLanguage", twice in
the records), and `move_type_to_namespace` moves one type at a time. Some of the 74 are path edits, not C# renames.

### 5. Royder's failures

50 calls, in 25 sessions:

- **Routing:** "Multiple solutions are open" without `solutionName`, 7 times, and a solution named that wasn't open.
- **Arguments:** `get_diagnostics` without `filePath`, 6 times. Agents wanted a whole-solution answer, which
  `inspect_code` gives. Also a missing `newNamespace`, and `reload_projects` given a `.slnx`.
- **Crashes:** `rename_symbol` cancelled (3, before the 18 November fix), `move_type_to_namespace` with
  `ArgumentOutOfRangeException` (1), and an `inspect_code` timeout.

## The first fix, for finding 1 (2026-11-24)

What went live, in UTC, so that `mine.py daily` can be read against it:

- **23 November, 23:20:** Royder's handshake instructions say which tool replaces which text lookup: "Look up C#
  symbols with go_to_definition, get_symbol_source, find_usages, not text search; list_symbols_in_file before
  reading a big file." They stay under 250 characters, the part Codex shows before its tool discovery. Those four tools
  carry `_meta: {"anthropic/alwaysLoad": true}`, so Claude Code loads them at a session's start instead of deferring
  them behind its tool search (`IMcpAlwaysLoadedTool`). The definitions add about 9,500 characters to every request of
  sessions whose project names Royder, which only C# projects do. Commits `2acab50` and `cccfc15`.
- **23 November, 23:26:** the always-loaded tool-use rule in `~/.claude` says what each C# lookup returns: a
  text search every spelling, comments and namesakes included; `go_to_definition` and `get_symbol_source` the
  declaration alone; `find_usages` only the references; `list_symbols_in_file` an outline before a whole-file read.
  It leaves the choice to the agent, as Magnus wants (2026-11-22). Codex's `AGENTS.md` was regenerated from it.
  `tooling-owner` trimmed the path-scoped C# rule to what the tool-use rule lacks. `~/.claude` commits `8c0518f1`
  and `10bcc9bd`.

Each reaches sessions started after it. A session started earlier keeps the old instructions until it restarts.

## Probes

`tools/agent_sessions/probe.py` puts a question from a mined episode to a fresh Claude Code session in the episode's
repository. The agent chooses its own tools, and is told only to change no files and to put nothing on the unanswered
list. Each run records the facts its answer holds, its cost, and its calls: Royder navigation, symbols looked up as
text, large files read whole. The runs are in `dev_docs/probe-runs.jsonl`, and the miner leaves probe sessions out.

~~~bash
python tools/agent_sessions/probe.py run --label "<what is live>"   # all probes; --probe <id> for one
python tools/agent_sessions/probe.py report
~~~

The probes are in `tools/agent_sessions/probes.json`, each with the episode it came from. They ask about the main
checkouts, so a probe's facts must be checked again when its code changes. They began after the first fix went live,
so they have no "before" run: the mined days from 16 to 23 November are the before. From now on, they show the effect
of later changes, and guard against regressions.

### First runs, 23 November from 23:30 UTC, after the first fix

| probe | facts | cost | seconds | characters taken in | Royder navigation | looked up as text |
|---|---|---|---|---|---|---|
| jastudio-note-serialization | 8/8 | $0.84 | 33 | 12,611 | 0 | 2 |
| deskmancer-startup-prewarm | 9/9 | $0.86 | 34 | 31,989 | 0 | 6 |
| compze-thread-gate | 5/5 | $0.72 | 23 | 24,780 | 1 | 2 |
| claude-tooling-session-inbox | 5/5 | $0.52 | 18 | 10,574 | 0 | 0 |

- **All four answered correctly, by text search and reads.** Of their 26 calls, one went to Royder's navigation,
  though Claude Code had loaded the four tools at the start (none was among the deferred) and Royder's instructions
  named them. On a short question, text search is fast, right and cheap.
- **What Royder would have saved is in what the session carries.** For the same facts, Royder answers with about
  2,200 characters where the JAStudio agent took in 12,600, and 4,300 where the Deskmancer agent took in 32,000; about
  the same for the other two. In a probe that is cents. In a long session, every later request reads it again: that
  is where the 35% of the mined cost was.
- **The one Royder call answered with the wrong part.** `get_symbol_source` for Compze's `IThreadGate`, an
  interface declared in two files, returned its private implementation's part, 11,042 characters, and the agent read
  `IThreadGate.cs` after it. ReSharper lists a type's parts in no useful order. Fixed in `1be07a6`: the tools show
  the part in the file named after the type (`MainDeclaration`).

So the first fix makes the navigation tools present and named, and the probe agents still chose text search. Whether
long sessions shift shows in `mine.py daily` over the coming days.
