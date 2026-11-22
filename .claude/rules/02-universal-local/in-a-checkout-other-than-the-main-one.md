# In a checkout other than the main one: deploy nothing, and edit only through a Rider that has your solution open

The main checkout is `C:\Dev\resharper-mcp`. Others are git worktrees of it, such as the Codex helpers' under
`C:\Dev\resharper-mcp.worktrees\` (`dev_docs/codex-helpers.md`). In one of those:

- **Deploy nothing.** `everyday deploy`, `everyday rollback` and `everyday rollback-plugin` change the Royder that
  every session on the machine uses, Magnus's Riders included: from a worktree, they would put work nobody has
  reviewed there. A change is done when it is committed on the checkout's branch, with the specifications passing.
  The session that handed you the work merges it into `main` and deploys it from the main checkout.
- **There is one sandbox Rider, taken in turns.** The specifications, `sandbox install` and `sandbox start` work from
  your checkout, with its build, but wait while another checkout's run holds the sandbox turn. Stop the sandbox when
  you are done with it (`sandbox stop`): it holds gigabytes.
- **Royder's tools reach your files only through a Rider that has your checkout's solution open.** Magnus opens one
  on each helper's checkout. A call that names a file in your checkout then goes to your solution, and its edits land
  in your files. A call that names no file, such as `search_symbol`, needs `solutionName`: several solutions named
  `ReSharperMcp` are open, and `list_solutions` gives each the segment of its path to name it by, such as
  `royder-codex-2`. Before any of the editing tools (`rename_symbol`, `move_type_to_namespace`, `fix_usings`,
  `format_file`, `apply_quick_fix`, `apply_suggestions`, `generate_members`), check with `list_solutions` that your
  checkout's solution is open. If it isn't, use none of them: they would edit the main checkout, in other sessions'
  work.
- **JetBrains' own `rider` MCP server:** use none of its editing tools. Which of Rider's open solutions it acts on
  hasn't been checked.
