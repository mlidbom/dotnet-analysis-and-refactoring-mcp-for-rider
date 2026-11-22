# Routing between headless Royder hosts

**The primary routes absolute paths to the correct live worktree, including a real edit. Two cases need changes for on-demand hosts.** A file in an unhosted worktree can go to the outer or sole solution. The file tools rejected it in this layout, after dispatch. A killed peer stayed registered even after three connection refusals.

## Run

```powershell
python -B -X utf8 experiments/HeadlessRouting/Check.py
```

Use Windows, Python with `psutil`, Git, PowerShell 7, and the [live host prerequisites](../HeadlessHost/README.md). The driver restores the two solutions it opens. It creates a shallow private Git repository at the current commit under `%TEMP%/royder-headless/royder-codex-3/routing-*` and three detached worktrees of that repository. The solutions have the same name, `ReSharperMcp`. Their fixture files have the same relative path and different class declarations.

The outer host starts first. Both hosts use base port 23920. The launcher checks that the expected port is free before each start, and the driver verifies the actual port, role and solution after indexing. A file lock serializes copies of the driver. Reserve 23920 and 23921 for the run. Production Royder can try up to ten consecutive ports on a bind failure, so a third-party collision after preflight would still be a race. The observed Royder listeners were 23920 and 23921. Each host has a separate app-data root, package directory, tools build, logs, temporary files and indexing cache outside its worktree.

The driver checks for **6 GiB available** before each host launch and samples available physical memory every half-second. A sample below **2 GiB** stops its owned processes. Before killing the peer, it verifies the engine PID from the `INDEXED` line against its launcher and solution argument.

The printed evidence directory contains `observations.jsonl` with the tool names, arguments, JSON-RPC replies, timings and disk comparisons. `host-*.log` contains the engine consoles, and `memory.jsonl` contains the memory samples. The worktrees are removed after restoring the probe. The committed transcript preserves the tool answers and MCP error flags for the distinct cases. [Recorded answers](answers.txt)

## Observed answers

Here `<outer>` and `<peer>` stand for sibling worktree roots, `<inner>` is `<outer>/.claude/worktrees/inner`, and `<probe>` is `tools/DotNetAnalysisAndRefactoringMcp.DevelopmentTools/RoutingProbe.cs`. Paths are abbreviated for readability.

| Case | What the caller received | Independent check |
| --- | --- | --- |
| 1. Both hosts registered | `list_solutions` on 23920 returned two entries named `ReSharperMcp` with 25 tools each. Their `path` fields pointed to the two solution files and their `uniquePathSegment` fields were `outer` and `peer`. | Using each segment as `solutionName` with the same relative file returned the corresponding class. The bare name `ReSharperMcp` produced an ambiguity error listing both segments. |
| 2. Absolute files | `list_symbols_in_file(<outer>/<probe>)` returned `sealed class OuterRoutingMarker :2`. The peer path returned `sealed class PeerRoutingMarker :2`. | The declarations differ in the fixture bytes. An echoed input filename would not establish where a call ran. |
| 2. Relative file in both trees | `The relative path exists in several open solutions: ReSharperMcp (<outer>), ReSharperMcp (<peer>). Pass 'solutionName' or an absolute path.` | A relative file existing only in the peer returned its `PeerRoutingMarkerOnly` declaration. |
| 2. No file, no name | `Multiple solutions are open. Specify 'solutionName' in the arguments, or pass a filePath that exists in only one of them.` The reply listed both solution paths. | An absolute file outside both worktrees produced the distinct router error `Path is outside open solutions`. |
| 3. Edit the peer through the primary | `fix_usings(<peer>/<probe>)` answered `added 1 usings:` followed by `using RoutingFixture.Dependencies; (for: RoutingRequiredType)`. | Across all three worktrees, the only changed file was the peer probe. Its new first line was the reported import. Restoring its original bytes reproduced the complete pre-edit snapshot. |
| 4. Sole outer host, unhosted sibling file | The tool returned `File is on disk but in no project of ReSharperMcp: <peer>/<probe>. Rider was asked to rescan it; if a project includes it (e.g. by a glob), retry in a moment, otherwise add it to a project.` | This was a tool payload containing an `error`, with MCP `isError: false`. The appended note said the file was on disk but in no project yet. No host was started. |
| 5. Sole outer host, unhosted nested file | Both `list_symbols_in_file` and `fix_usings` returned the same no-project error for `<inner>/<probe>`. Neither call returned the outer declaration or reported an import. | All three worktrees retained the same tracked and nonignored file bytes. A second read while the unrelated sibling was hosted also reached file lookup and received the no-project error. |
| 6. Killed peer | The primary still listed the peer after 65 seconds with no calls addressed to it. `/health` reported the peer as `reachable: false`. Three calls naming its absolute file each returned `isError: true` and a proxy failure. The peer was still listed after the third. | The primary logged an inner `SocketException (10061)`. The separate .NET 10 HTTP probe against the closed 23921 endpoint reported `WebException.Status=UnknownError, SocketErrorCode=ConnectionRefused, NativeErrorCode=10061`. |
| 6. Recovery and actual removal | A fresh peer on 23921 registered the same solution path and served `PeerRoutingMarker` again. After a normal exit, `list_solutions` immediately showed only the outer solution. Its next read of the peer file gave the single-host no-project error from case 4. | The crash had no observed automatic removal time. Replacement followed by graceful deregistration is the measured path to a one-entry list. |

Each failed forwarding attempt returned this text.

```text
ReSharper failed on the call proxied to the peer on port 23921: WebException: No connection could be made because the target machine actively refused it. [::ffff:127.0.0.1]:23921 (127.0.0.1:23921)
```

### Recorded run

The complete run on 22 November 2026 used commit `aee4ed2` for the private worktrees. Its evidence is in `%TEMP%/royder-headless/royder-codex-3/routing-5_dbiqjc/`.

| Observation | Measurement |
| --- | --- |
| Sole-host reads of the unhosted sibling and nested files | 10.112 s and 10.020 s. The subsequent nested edit refusal took 0.056 s. |
| Actual peer edit | 0.658 s until the reply. The driver compared all three worktrees after the reply and after restoration. |
| Registrations after killing PID 198896 | Still two entries at 0.285 s and 65.003 s. After each failed forwarding attempt, still two at 68.052 s, 70.070 s and 72.078 s. |
| Read after graceful deregistration | Only the outer registration remained. The peer-file read took 10.061 s and returned the no-project error. |
| Physical memory | The samples closest to engine creation showed 9.89 GiB available for the primary, 8.47 GiB for the peer and 26.62 GiB for the replacement. The lowest sample over the run was 7.62 GiB. |

Both ports had no listener at the end. All three engine PIDs exited and the private Git worktrees were removed. An earlier independent run also retained the dead peer after three refusals and removed the replacement on graceful exit.

## Why it behaves this way

- `McpHttpServer.RegisterPeer` keys registrations by solution path. `OpenSolutions.UniquePathSegments` selects a directory segment that distinguishes solutions of the same name. Registering a replacement for the same path updates that registration. [McpHttpServer.cs](../../src/DotNetAnalysisAndRefactoringMcp/McpHttpServer.cs) · [OpenSolutions.cs](../../src/DotNetAnalysisAndRefactoringMcp.Tools/Serving/OpenSolutions.cs)
- `OpenSolutions.Choose` handles an explicit `solutionName` first and unconditionally selects a sole open solution next. Only when several are open does it examine `FileArguments.Of`. For absolute paths, `OpenSolution.Holds` checks a directory prefix and `Closeness` ranks matching roots. Relative paths use file existence. A nested, unhosted Git worktree still has its parent's prefix. [OpenSolutions.cs](../../src/DotNetAnalysisAndRefactoringMcp.Tools/Serving/OpenSolutions.cs) · [OpenSolution.cs](../../src/DotNetAnalysisAndRefactoringMcp.Tools/Serving/OpenSolution.cs)
- In this repository the nested worktree sits outside the outer solution's project directories. `PsiHelpers.ResolveFile` cannot find its absolute file among those project items. That later check prevented an incorrect symbol answer or write in the tested layout. Its rescan advice also assumes a Rider frontend. [PsiHelpers.cs](../../src/DotNetAnalysisAndRefactoringMcp.Tools/PsiHelpers.cs)
- `PeerSolution.Health` reports unreachability without changing registrations. `PeerSolution.Call` removes peers only when the outer `WebException.Status` is `ConnectFailure`. The observed refusal took the generic error branch. The primary's watchdog pings its own listener. Normal peer shutdown explicitly sends `internal/deregister` with the solution path. [PeerSolution.cs](../../src/DotNetAnalysisAndRefactoringMcp.Tools/Serving/PeerSolution.cs) · [PrimaryServer.cs](../../src/DotNetAnalysisAndRefactoringMcp.Tools/Serving/PrimaryServer.cs) · [McpShellComponent.cs](../../src/DotNetAnalysisAndRefactoringMcp/McpShellComponent.cs)

## Implications for on-demand hosts

1. Resolve the owning Git worktree of absolute file arguments **before the sole-solution shortcut**. An unhosted nested worktree must be distinguishable from an ordinary subdirectory of a hosted worktree. Start its host or return an explicit unhosted-worktree response. Keep the existing disambiguation among solutions within the identified worktree and for shared relative paths.
2. Drop or revalidate the specific registration when its process exits or forwarding establishes that the endpoint refused the connection. Include host identity in removal so that a delayed failure from an old PID cannot evict a replacement. A slow call alone is insufficient evidence of a dead process.
3. Perform the ownership check before dispatching edits. The absence of damage here depended on project membership. These cases do not establish what happens when a project deliberately links a file from another worktree, or includes nested worktree files in its compile items.
