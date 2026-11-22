# On-demand Royder for linked Git worktrees

The standalone primary serves the ordinary MCP tools when every IDE is closed. An absolute file argument in a linked
worktree opens its solution in a shared private Rider JVM. Calls for an open ordinary Rider solution still go to
that Rider, including its unsaved-document handling. The primary uses the same solution-name and file-path routing
as the plugin. Each linked worktree must belong to one of the main repositories trusted in the primary settings.

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
    "remoteDevPort": 24931,
    "backendMcpPort": 24941
  }
}
~~~

Choose a restored solution in a disposable linked worktree of a listed repository. Preparing the catalog briefly
opens that solution, captures its real MCP handshake and tool definitions and shuts the private JVM down. A matching
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

~~~powershell
./tools/Register-RoyderPrimaryAtLogon.ps1 -PrimaryExecutable $primary -SettingsFile $config
Start-ScheduledTask -TaskName RoyderPrimary
~~~

## Call and removal behavior

- A cold `tools/list` works immediately. File tools open a registered linked Git worktree on first use. Use an
  absolute `.sln` or `.slnx` in `solutionName` when solution discovery is ambiguous. Use `warm_worktree` with
  `solutionPath` to start it explicitly. Concurrent callers share one open. The first call waits up to 60 seconds.
  On timeout only the warm-up continues. Tool calls, including edits, are dispatched once.
- A newly opened solution must report finished indexing and source generator synchronization or confirmed
  non-applicability. The primary checks that the staged tools are serving. Its health response also lists the
  worktrees currently opening.
- `close_worktree` closes an agent project and waits for its workers. Unused agent projects close after ten minutes.
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
and release of the final idle host. Run with `--check-timeout` instead to verify a timed-out cold edit never executes.

~~~powershell
python -B tests/headless/verify_on_demand.py --primary C:/Royder/packages/current-build/Royder.Primary.dll --assets C:/Royder/packages/current-build/RiderHost --check-replacement --check-idle
~~~

The ordinary Rider regression suite is `dotnet test tests/DotNetAnalysisAndRefactoringMcp.Specifications`.
With an ordinary Rider running, `tests/headless/verify_ordinary_routing.py` also compares the two endpoints'
solution lists and JSON declarations for a specified solution and symbol.
