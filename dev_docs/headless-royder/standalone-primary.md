# On-demand Royder for trusted Git checkouts

The standalone primary serves the ordinary MCP tools when every IDE is closed. An absolute file argument opens the
owning solution in a shared private Rider JVM. This works in each main checkout listed in `repositories` and in
its registered linked worktrees. Calls for a checkout open in ordinary Rider go to that Rider, including its
unsaved-document handling.

The private JVM starts without opening a solution. For each request to open one, it creates an IntelliJ project
with its own settings directory in the private run folder and initializes Rider's backend with the real solution
filename. Navigation and edits use the original checkout. The headless project saves its own workspace state;
the desktop's tabs, workspace and project settings stay in the checkout.

## Prepare an installation

On Windows with the installed Rider, Royder plugin, .NET SDK and PowerShell 7.4+, build a fresh package.

~~~powershell
./tools/Build-HeadlessRoyder.ps1 -OutputDirectory C:/Royder/packages/current-build
~~~

Use a new directory for each build. Its `RiderHost/package.json` names the pinned tools build. Put the following
settings in a JSON file, substituting the local paths, **the exact build string from that manifest**, and available
ports. Use a dedicated profile directory.

~~~json
{
  "port": 24930,
  "desktopPort": 23751,
  "repositories": ["C:/Dev/resharper-mcp"],
  "catalogFile": "C:/Royder/state/catalog.json",
  "rider": {
    "profileDirectory": "C:/Royder/state/agent-rider",
    "riderHome": "C:/Users/magnu/AppData/Local/Programs/Rider",
    "assetsDirectory": "C:/Royder/packages/current-build/RiderHost",
    "toolsBuild": "<build from package.json>",
    "toolsFolder": "C:/Users/magnu/AppData/Local/DotNetAnalysisAndRefactoringMcp/tools",
    "remoteDevPort": 24931,
    "backendMcpPort": 24941
  }
}
~~~

`toolsFolder` is optional. It names a folder of tools builds, here the one `everyday deploy` deploys to: the private
Rider serves its newest build for the packaged host, and swaps to a newer one between calls, as the desktop Riders
do. A tools change then reaches the checkouts the primary opens without a new package. The pinned build is served
while that folder has none for the host, such as after a host change the package doesn't have yet. Without
`toolsFolder`, the private Rider serves the pinned build only, which is what capturing a catalog and the verification
below need.

Choose a restored solution in a trusted main checkout or linked worktree. Preparing the catalog briefly
opens that solution, captures its real MCP handshake and tool definitions and shuts the private JVM down. If ordinary
Rider already owns that checkout, capture uses its endpoint and verifies the build there. A matching
catalog can also be captured from a running sandbox with `capture-catalog <port> <build> <catalog-file>`.

~~~powershell
$primary = 'C:/Royder/packages/current-build/Royder.Primary.exe'
$config = 'C:/Royder/state/primary.json'
& $primary prepare-catalog $config C:/Dev/my-agent-worktree/ReSharperMcp.sln
& $primary serve $config
~~~

The listener binds only `127.0.0.1`. Keep the existing MCP server key and use the primary's URL. The example puts
the primary next to the everyday Rider endpoint so it can be tried without restarting an IDE. For an everyday rollout
on the existing public port, install `%LOCALAPPDATA%/DotNetAnalysisAndRefactoringMcp/primary/settings.json` with
`port` set to 23751 and `desktopPort` set to 23761. At startup the Rider plugin reads the desktop port from that
file. An explicit `DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT` still takes precedence for isolated Riders. Each backend
cluster reserves ten consecutive MCP ports. Omit `desktopPort` on an installation with no ordinary Rider.

To run the primary at Windows sign-in, register the task under the current user's account. The command leaves
the task ready. For a cutover on 23751, start the task after the old ordinary Rider releases that port.

The task runs the primary in a console with no window: `conhost.exe --headless`. A console program the task
starts otherwise gets its console from the default terminal, Windows Terminal on Magnus's machine, whose window
`-WindowStyle Hidden` only minimizes. On 23 November 2026 that window, titled with `powershell.exe`'s path, was
closed as a stray one, twice, and closing it ended the primary with `0xC000013A`, leaving every session without
Royder.

The task also starts the primary again within a minute whenever it has ended, while the user is signed in: a
second trigger fires every minute, and does nothing while the primary runs. Task Scheduler's restart settings
cover only a start that fails. To keep the primary stopped, `Disable-ScheduledTask -TaskName RoyderPrimary`
first. Each run's log, under `logs/`, starts with the time the task started it and the time the machine started,
and ends with the primary's exit code, which the headless console host doesn't pass on to the task.

~~~powershell
./tools/Register-RoyderPrimaryAtLogon.ps1 -PrimaryExecutable $primary -SettingsFile $config
Start-ScheduledTask -TaskName RoyderPrimary
~~~

For subsequent local updates, stage the new package and its verified `catalog.json` together in a new
directory. `tools/Update-InstalledRoyderPrimary.ps1 -PackageDirectory <directory>` asks an idle primary to
shut down, waits for requests to drain, updates the sign-in task and checks the new public listener. It keeps
the old settings for rollback. Earlier packages that lack the `stop` command require a coordinated stop first.

A new package is needed for a change to the primary, the host or the management plugins, not for a tools change.
`-ToolsFolder <folder>` adds `toolsFolder` to the settings; an update keeps the one they name.

On this machine, `tools/New-RoyderPrimaryPackage.ps1 -Install` does all of it, from the checked-out commit:

~~~powershell
./tools/New-RoyderPrimaryPackage.ps1 -Install
~~~

It builds into `%LOCALAPPDATA%/DotNetAnalysisAndRefactoringMcp/primary/<commit>`, then captures the catalog with a
temporary settings file beside it. That file has no `desktopPort`, so the capture opens its own private Rider rather
than verifying the desktop's build. It uses ports other than the running primary's (24930, 24931 and 24941) and a
profile directory of its own, about 600 MB, deleted afterwards with the settings file. `catalogFile`,
`assetsDirectory` and `toolsBuild` are the package's, and it names no `toolsFolder`, so the capture checks the
package's own build. The update refuses while the primary has a managed project open; `-Install` retries it until
they have closed, which they do after 30 idle minutes (`idleSeconds`), for an hour at most.

## Call and removal behavior

- A cold `tools/list` works immediately. File tools open a trusted main checkout or linked worktree on first use. Use an
  absolute `.sln` or `.slnx` in `solutionName` when solution discovery is ambiguous. Use `warm_worktree` with
  `solutionPath` to start it explicitly. Concurrent callers share one open. Tool calls, including edits, are
  dispatched once.
- Every tool call is answered within `answerWithinSeconds`, 55 by default, since Claude Code gives up on an MCP call
  after 60 seconds and then tells the agent only "The operation timed out.". A cold open still running by then answers
  that the solution is still opening, and only the warm-up continues. A call Rider is still working on answers that
  it goes on in Rider: an edit in it still lands. The 60 seconds is Claude Code's default per-request limit for an
  HTTP server; `MCP_TOOL_TIMEOUT`, or a `timeout` on the server's entry in `.mcp.json`, raises it. The tooling owner
  kept the default (2026-11-24): "still opening, retry" tells an agent more than a long wait.
- `list_solutions` lists solutions open at the time of the call. Its `hint` explains how to open another trusted
  checkout, including when the list is empty. The primary checks the local TCP listener table before contacting
  ordinary Rider, so a closed desktop endpoint adds no connection-refusal wait.
- A newly opened solution must report finished indexing and source generator synchronization or confirmed
  non-applicability. The primary checks that the staged tools are serving. Its health response also lists the
  worktrees currently opening.
- `close_worktree` closes an agent project and waits for its workers. Unused agent projects close after 30 minutes (`idleSeconds`), and the private Rider with the last of them: an agent calling every 10 to 20 minutes otherwise met a cold start nearly every time.
  Ordinary Rider stays running. To close and remove a worktree under the same lifecycle operation, run
  `Royder.Primary remove-worktree <settings.json> <absolute-worktree-root>`. The command uses a Windows named pipe restricted to the current user. Git uses its ordinary clean-worktree
  checks. This command refuses a main checkout, an untrusted repository, a changed registration and a worktree
  open in ordinary Rider.
- External Git removal is observed from the registered worktree identity. The manager discards the old private
  project's settings when closing it after removal or replacement. A persistent journal associates the private
  projects with the original Git registrations across primary restarts. Shutdown never recursively deletes a
  checkout or sweeps a recreated directory.

[Shared-JVM measurements](rider-remote-dev-server.md#round-9--two-worktrees-in-one-rider-jvm-22-november-2026)

## Verification

With a built package, the following test creates a disposable repository and exercises the public HTTP endpoint.
It checks two racing first calls, generated source references in each worktree, independent removal and replacement,
and release of the final idle host. Include `--main-checkout` to exercise a real rename in the main checkout, check
its existing project settings byte-for-byte and timestamp-for-timestamp, and verify that a removal request leaves
it open. Run with `--check-timeout` to verify a timed-out cold edit never executes.

~~~powershell
python -B tests/headless/verify_on_demand.py --primary C:/Royder/packages/current-build/Royder.Primary.dll --assets C:/Royder/packages/current-build/RiderHost --check-replacement --check-idle
~~~

The ordinary Rider regression suite is `dotnet test tests/DotNetAnalysisAndRefactoringMcp.Specifications`.
With the same installed settings, `tests/headless/verify_cold_discovery.py` exercises cold discovery, measures
requests with an absent ordinary endpoint, and verifies detection when that endpoint appears and disappears.
With an ordinary Rider running, `tests/headless/verify_ordinary_routing.py` also compares the two endpoints'
solution lists and JSON declarations for a specified solution and symbol.
