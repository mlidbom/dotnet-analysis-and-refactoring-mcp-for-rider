# Codex helpers: checkouts of their own, briefed and merged from the main one

A Claude session here hands most of its work to Codex helpers, and reviews and merges what they do. Magnus,
2026-11-22: "please talk to gpt-collaborators about how to work with codex by delegating most work to them." What
briefing and reviewing a helper takes anywhere is in `~/.claude/dev_docs/working-with-codex-helpers.md`: read it before
the first brief. What a helper's checkout can't do is in the rule "In a checkout other than the main one".

## The helpers

- `royder-codex-1`, in the main checkout, `C:\Dev\resharper-mcp`, which three Claude sessions share: work that edits
  no tracked file, such as research, reading assemblies and measuring.
- `royder-codex-2` and `royder-codex-3`, in `C:\Dev\resharper-mcp.worktrees\royder-codex-2` and `-3`, on the
  branches of their names, made 2026-11-22 from `main`. An absolute file path in the helper's checkout or its
  full `ReSharperMcp.sln` as `solutionName` opens that solution on demand, even with desktop Rider closed.

To make another:
`git worktree add -b royder-codex-4 C:/Dev/resharper-mcp.worktrees/royder-codex-4 main`, then in it
`sh .claude/attach-shared-claude-config.sessionstart-hook.sh`, which gives it the shared catalog, and
`~/.claude/bin/ClaudesSetupForCodex.exe project <its folder>`, which writes Codex's rules, skills and MCP servers
there (`AGENTS.override.md`, `.agents/skills`, `.codex/config.toml`, which `.git/info/exclude` keeps out of git). Run
that again in every checkout after a rule changes.

**Starting one** is Magnus's: `cd` into the checkout, `codex`, trust the folder, `/rename royder-codex-2`, and a first
prompt such as "You're a helper for headless-royder. Its briefs come as turns of their own: end this one now." It
shows in `list_peers` once it has had a turn.

## Handing one a task

1. Bring its branch up to `main` while it has nothing unmerged: `git -C <its checkout> merge --ff-only main`.
2. Brief it with `send_to_peer` to `codex:<its id>`: a brief that stands alone, its points numbered, the limits in
   so many words (among them that it reaches other sessions, the tooling owner's and the symbol linker's included,
   only through you: royder-codex-1 wrote to the tooling owner on its own, 2026-11-22), what done is (committed on its branch, permanent specifications in
   `tests/DotNetAnalysisAndRefactoringMcp.Specifications`, passing with the rest), and what to send back (its commits,
   what it ran and the result, what it verified and what it inferred, what it noticed and left).
3. Review its commits once it replies, `git log main..royder-codex-2` and `git diff main...royder-codex-2`, for design
   and the code standards above all. Ask for changes, or make them.
4. Merge: rebase its branch onto `main` if `main` has moved, then `git merge --ff-only royder-codex-2` in the main
   checkout. Run the specifications there, deploy, and push.

**A message to a busy helper runs after its turn,** as a turn of its own, so it can cross the helper's report. A
correction sent during a turn reached royder-codex-3 after it had reported the work done, and it set about redoing
merged work, three times on 2026-11-22. So:
- Steer a busy helper only when the turn under way would waste much without it.
- Name in each message the commit it concerns ("about 42ee55c: ...").
- After a merge, tell the helper that the merged commit stands, and that any message older than it is void.
