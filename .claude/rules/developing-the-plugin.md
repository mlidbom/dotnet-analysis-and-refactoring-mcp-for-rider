# Developing the plugin: the sandbox Rider, deploying, branches

This repository is Magnus's fork of Joshua Light's plugin, renamed .NET Analysis&Refactoring MCP, and a development
tool you own: see the user-level rule "The dotnet-analysis-and-refactoring MCP is a development tool you own". What it should do better is in
`dev_docs/backlog.md`.

The plugin is two assemblies: the host, which Rider loads at startup, and the tools, which the host swaps for a newer
build between calls, without a restart. `dev_docs/swappable-tools.md` says how, and the rules code in the tools
follows so that a swapped-out build can be freed.

Three words, kept apart: a tools build is **deployed** where running Riders serve it from their next call; the plugin
is **installed** in a Rider's plugins folder, which takes effect at its restart; **publishing** is uploading it to
the Marketplace, which needs Magnus's word.

The development tools: `dotnet run --project tools/DotNetAnalysisAndRefactoringMcp.DevelopmentTools -- <command>`; with
no command they list the commands.

## Test every build in the sandbox Rider before it goes anywhere else

The everyday Riders, Magnus's, run the build that every session on the machine uses, this one included. A broken build
there takes away the code intelligence you would fix it with, and can hang the IDE he works in. So a build is tested in
the sandbox Rider first: a second Rider, with folders of its own, its own tools builds, and the MCP server on port 23841.

- `dotnet test tests/DotNetAnalysisAndRefactoringMcp.Specifications` is the test: it puts the working tree's build in
  the sandbox, opens a fresh copy of `tests/FixtureSolution` there, and checks the tools' answers over HTTP, and that
  swapped-out tools builds are freed. A run takes under a minute; NCrunch is told to ignore the project. A known bug
  gets a skipped specification that names it, so that fixing it means removing the `Skip`.
- `sandbox install` builds the working tree and puts it in the sandbox: only the tools, served from the next call,
  unless the host changed; then the whole plugin, restarting the sandbox if it runs.
- `sandbox start <solution>` opens a solution in it and returns once the MCP server has it indexed. Call the tools on
  `http://127.0.0.1:23841/`; `/health` says which tools build serves them.
- The sandbox's window goes, by itself, to the virtual desktop of the Rider that has this repository's solution open,
  on the other monitor, small. `sandbox status` says where it is.

## Deploying to the everyday Riders: the tools at once, the host at a restart

`everyday deploy` deploys a build that passed in the sandbox: every everyday Rider serves its tools from its next
call. Only if the host changed does it install the plugin too, which waits for Magnus to restart his Riders: then
deploy in batches, and end the report with the heading `### RESTART RIDER ###` and a line saying what the restart
brings. `everyday rollback` removes the newest tools build, so the Riders go back to the one before it at their next
call; `everyday rollback-plugin` puts back the plugin build that the last host change replaced.

The original plugin (`resharper-joshua`, port 23741) was removed from Magnus's Riders on 2026-11-14, once his symbol
linker depended on this one. Every project's `.mcp.json` names this one, `dotnet-analysis-and-refactoring`, since then.

## Releasing: as the symbol linker is released

The build is Gradle's (`rider-plugin/build.gradle.kts`), which also builds the .NET part, and the release is the
.NET Terminal Symbol Linker's: `pluginVersion` in `rider-plugin/gradle.properties`, the version's section in
`CHANGELOG.md`, and a `v<version>` tag, from which `.github/workflows/release.yml` builds, signs, creates the GitHub
Release and publishes to the Marketplace. Pushing a release tag publishes, so it needs Magnus's word. `verifyPlugin` runs the Marketplace's Plugin Verifier and fails on
any finding, in CI and before a release: updates with none have been approved at once (Magnus, 2026-11-14). Keep the two
plugins' release setups alike (Magnus, 2026-11-14): change one, and say whether the other should follow.

## Branches: `main` is ours, a pull request's branch is upstream's

- `main` is upstream's `main` plus our work. Commit and push it freely.
- A pull request to upstream goes on a branch of its own, cut from `upstream/main`, with nothing but its change: the
  maintainer sees every commit on it. Since the rename, a fix only goes upstream when it applies to upstream's code as
  it is.
