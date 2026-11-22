# Working in a linked worktree

The main checkout is `C:\Dev\resharper-mcp`. Others are git worktrees of it, such as the Codex helpers' under
`C:\Dev\resharper-mcp.worktrees\` (`dev_docs/codex-helpers.md`). In one of those:

- **Deploy nothing.** `everyday deploy`, `everyday rollback` and `everyday rollback-plugin` change the Royder that
  every session on the machine uses, Magnus's Riders included: from a worktree, they would put work nobody has
  reviewed there. A change is done when it is committed on the checkout's branch, with the specifications passing.
  The session that handed you the work merges it into `main` and deploys it from the main checkout.
- **There is one sandbox Rider, taken in turns.** The specifications, `sandbox install` and `sandbox start` work from
  your checkout, with its build, but wait while another checkout's run holds the sandbox turn. Stop the sandbox when
  you are done with it (`sandbox stop`): it holds gigabytes.
- **Target your checkout in the Royder call itself.** For a file operation, pass the absolute path in your
  worktree. For a symbol by name or a solution-wide operation, pass your worktree's absolute
  `ReSharperMcp.sln` path as `solutionName`. This applies to edits too. Royder opens that checkout on
  demand when desktop Rider is closed. `list_solutions` shows which solutions are already open.
- **JetBrains' own `rider` MCP server:** use none of its editing tools. Which of Rider's open solutions it acts on
  hasn't been checked.
