# Shared Rider on Windows

`SharedRiderHost` starts a private Rider JVM when the first managed solution opens. The primary routes requests
to that solution. Subsequent opens and closes use the lifecycle plugin in that JVM. Call `StopAsync` to stop the
entire generation. It is safe to call again after a last-project close.

## Package the runtime assets

From the repository root, stage the assets alongside the published primary. Use the repository revision containing
the lifecycle plugin’s open, normal-close and discard-close handlers.

```powershell
dotnet publish ./src/Royder.Primary -c Release -o $publishDirectory
./src/Royder.Primary/Hosting/Stage-SharedRider.ps1 `
   -OutputDirectory (Join-Path $publishDirectory RiderHost) `
   -ToolsBuild '2026-11-22_04-26-01-2911330'
```

Stage once in a fresh destination per package. The script builds the JVM lifecycle plugin and the .NET Roslyn patcher,
copies the direct-Java launcher, Royder plugin and selected tools, and records the installed Rider build and assembly
hashes. `-RiderHome`, `-LifecyclePlugin`, `-RoyderPlugin` and `-ToolsDirectory` accept explicit build inputs.
`-ToolsDirectory` names the directory containing `host.sha256` and the tools DLL. The defaults read the installed
user plugin and named deployed tools build. The running primary uses the **staged copies**.

Construct `SharedRiderSettings(ProfileDirectory, RiderHome, AssetsDirectory, ToolsBuild, RemoteDevPort,
BackendMcpPort, HeapMiB)` with absolute paths. `AssetsDirectory` is the staged `RiderHost` directory.
Use a dedicated, private, current-user profile and disjoint loopback port ranges. Reserve the RD port and ten
ports starting at `BackendMcpPort` for this host, separately from the public primary. Use a loopback HTTP client
with proxies and automatic redirects disabled for MCP health/registration. The default heap is 768 MiB.
The launch profile requires PowerShell 7.4+ x64. Each managed JVM has private settings, temp files and a private
Roslyn working directory. [Launcher behavior](../../../experiments/RemoteDevHost/README.md)

## Ownership and recovery

- A detached supervisor starts Java suspended in a kill-on-close Windows job before resuming it. It owns
  `host.lock`, the port reservations and the job handle. The primary serializes operations on the profile. A
  separate lease covers an already dispatched command while its initiating primary is exiting.
- The supervisor records the owning primary’s PID and creation time. A replacement primary can adopt the
  same JVM on its next operation. After 60 seconds without a live owner, the supervisor requests shutdown and
  drains the job. Killing the supervisor closes the job handle. A prior JVM, observed worker or held profile
  lock prevents starting a replacement generation until its termination can be established.
- The management bearer is random per JVM. It reaches the plugin through the child environment. The
  supervisor stores a Windows DPAPI current-user encrypted copy in that generation’s directory so the next
  primary can adopt it. Before sending the bearer, the launcher opens a loopback TCP socket to the announced
  IntelliJ built-in HTTP port, verifies the connected peer's PID and JVM creation time, and sends the request
  on that exact connection. Its client disables proxies and redirects.
- Close records the target incarnation before dispatch. A backend identity requires current job membership,
  PID/creation time and the exact solution-open entry from that backend's own Rider log. The supervisor records
  parent PIDs as it observes members. After close the adapter requires fresh job membership, waits for the
  backend and its descendants to disappear and reports any surviving or unaccounted identities. A successful
  HTTP close alone does not release the worktree for removal. The managed JVM starts with no project.
  Its authenticated opener creates each private IntelliJ project and returns its strong ETag. Each run stores the exact tag and uses it as `If-Match`
  for normal and discard closes. A veto, disk replacement on normal close, tag mismatch or missing identity
  is an error. A request already dispatched is allowed to finish even if the caller cancels.
- When the last project closes Rider can retain an empty backend in the lobby. The adapter confirms the private
  JVM's project inventory is empty and no sibling backend is alive, then stops the whole job. For surviving
  projects it waits for the private MCP primary watchdog's promotion if needed and re-registers missing peers
  from verified local backend health and tool catalogs. Readiness requires the exact solution, run-specific
  tools directory, build and host hash. The primary waits separately for indexing and generator synchronization, so other projects can close during that wait.

The profile retains a per-generation plan, private logs, PID history and shutdown evidence. Raw discovery and
backend logs can carry private project data. Package staging installs nothing in everyday Riders and registers no
scheduled task.

## Verification on Rider RD-263.7307.325

Three isolated solutions named `SharedProbe.sln` shared one private JVM. The first launch and a second adoption of
that project returned the same project tag. Two authenticated opens brought the sibling solutions online. Closing
the first backend triggered primary promotion. A replacement manager resumed the close from its recorded process
identity and restored the missing sibling registration, after which both remaining paths appeared on the base port.
After an external rename removed a sibling's solution file, normal close returned 409. Discard with its original
tag succeeded, the missing file stayed absent, and the remaining solution was still reachable. Closing the last
project drained the whole job. The isolated run used RD 25931, backend MCP 25941 and tools
`2026-11-22_04-26-01-2911330`. This older tools build does not report the optional generator-readiness field.
