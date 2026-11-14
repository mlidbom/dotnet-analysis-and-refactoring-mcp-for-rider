# Developing the plugin: the sandbox Rider, deploying, branches

This repository is Magnus's fork of Joshua Light's plugin, and a development tool you own: see the user-level rule
"resharper-joshua is a development tool you own". What it should do better is in `dev_docs/backlog.md`.

The development tools: `dotnet run --project tools/ReSharperMcp.DevelopmentTools -- <command>`; with no command they
list the commands.

## Test every build in the sandbox Rider before it goes anywhere else

The everyday Rider, Magnus's, runs the build that every session on the machine uses, this one included. A broken build
there takes away the code intelligence you would fix it with, and can hang the IDE he works in. So a build is tested in
the sandbox Rider first: a second Rider, with folders of its own and the MCP server on port 23841.

- `sandbox install` builds the working tree and installs it in the sandbox, restarting the sandbox if it runs.
- `sandbox start <solution>` opens a solution in it and returns once the MCP server has it indexed. Call the tools on
  `http://127.0.0.1:23841/`.
- The sandbox's window goes, by itself, to the virtual desktop of the Rider that has `ReSharperMcp` open, on the other
  monitor, small. `sandbox status` says where it is.

## Deploying to the everyday Rider: it takes effect when Magnus restarts it

`everyday deploy` installs a build that passed in the sandbox; `everyday rollback` puts back the one it replaced. A
deployed build waits for Magnus to restart his Rider, which every session shares, so deploy in batches, and end the
report with the heading `### RESTART RIDER ###` and a line saying what the restart brings.

## Branches: `main` is ours, a pull request's branch is upstream's

- `main` is upstream's `main` plus our work: the tools, the documents, our fixes. Commit and push it freely.
- A pull request to upstream goes on a branch of its own, cut from `upstream/main`, with nothing but its change: the
  maintainer sees every commit on it.
- Upstream is contributed to while that helps. When it hinders, such as a feature that needs restructuring upstream
  wouldn't take, the fork is renamed and released by us (Magnus, 2026-11-14: at the latest with the first such
  feature).
