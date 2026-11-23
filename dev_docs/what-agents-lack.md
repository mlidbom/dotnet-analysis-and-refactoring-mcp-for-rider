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
