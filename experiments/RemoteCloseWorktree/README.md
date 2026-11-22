# Manage worktrees in a shared Rider server

This JVM plugin lets Royder's primary open a solution on demand, save and close it before Git removal, or discard
the original open instance after external removal or replacement. Install it only in the agent server's private
plugins directory. It has its own plugin ID and JAR and can be staged alongside the [Roslyn patcher](../RemoteRoslynPatcher/README.md).

## Build and verify

Run from the repository root in PowerShell 7 on Windows.

```powershell
./experiments/RemoteCloseWorktree/Build.ps1 -Verify
```

The script checks for **RD-263.7307.325** at `%LOCALAPPDATA%\Programs\Rider`. Pass `-RiderHome` for another copy of
that build. Its bundled Java compiler reads Rider's libraries and produces Java 21 bytecode. The installable folder
is `experiments/RemoteCloseWorktree/bin/plugin/RemoteCloseWorktree/` with a single `lib/RemoteCloseWorktree.jar`.

Every build tests solution rewrites, replacement Git markers and replacement directories in a private filesystem
fixture. The discard decision specs check a veto, unsaved documents, changes during the veto check and a clean
discard. Omit `-Verify` to skip Plugin Verifier. Verification uses the locally cached Plugin Verifier CLI 1.411
from Gradle or an explicit `-VerifierJar <path>`. It runs offline with its home, AppData, temporary files and reports
under the experiment's `obj/verifier/<run>/`. The script prints the verdict file and requires `Compatible`.

## Install in an agent server

Before starting a private server, copy the **whole plugin folder** into the directory that its launcher sets as
`idea.plugins.path`. This example uses the launcher's existing `$privatePlugins` directory and its child environment.

```powershell
$package = (Resolve-Path 'experiments/RemoteCloseWorktree/bin/plugin/RemoteCloseWorktree').Path
$destination = Join-Path $privatePlugins 'RemoteCloseWorktree'
if (Test-Path -LiteralPath $destination) { throw "A plugin already exists at $destination" }
Copy-Item -LiteralPath $package -Destination $destination -Recurse

$hostToken = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$serverStartInfo.Environment['ROYDER_MANAGED_HOST_TOKEN'] = $hostToken
```

Give the same token to the primary authorized to manage that server. A missing, empty or whitespace-only
`ROYDER_MANAGED_HOST_TOKEN` disables all management requests. The handlers also require a loopback peer and Rider's
ordinary built-in HTTP trust checks. Keep the token in the management processes.

## Call from the primary

Send POST requests to the **built-in HTTP port of the target host JVM**. The worktree supervisor
can set `-Drpc.port` in that JVM's options and verify the listening socket belongs to its PID before passing the port
to the primary. Rider can fall back to another port. Its JVM reports the actual port through
`BuiltInServerManager.getInstance().waitForStart().getPort()`. Royder's MCP port and the remote-dev listener serve
other protocols.

After authorizing the caller for the target worktree, use the exact absolute solution filename in that worktree's
registration. Send the path as UTF-8 plain text without quotes, BOM or trailing newline. Two worktrees may have
identically named solutions. Use a direct loopback client with HTTP redirects disabled. The primary authorizes
the Git worktree and serializes lifecycle transitions for that registration.

| POST operation | Behavior |
|---|---|
| `/api/royder-open-worktree` | Open a local physical `.sln` or `.slnx` in this JVM, or adopt the matching open project. Concurrent opens of the same normalized path share one operation. A 200 carries a **quoted strong ETag** for this IntelliJ project instance. Adopt the first, launcher-opened solution once its backend has registered. Store the exact tag with the supervisor's Git worktree generation. |
| `/api/royder-close-worktree` | Normal save and close. Send the stored ETag as `If-Match`. Existing callers can close by unambiguous path alone. A missing solution or a different registered directory or Git marker returns 409 before the saving close. A legacy project which was never adopted has no recorded disk identity. |
| `/api/royder-close-worktree/discard` | Administrative close when the supervisor knows Git has removed or replaced the tree. **Requires `If-Match`**. The old Project and its ETag stay available after deletion. The handler checks for unsaved source documents, calls Rider's close vetoes, rechecks for unsaved documents, and calls `ProjectManagerEx.forceCloseProject(project, false)` to dispose without saving. It does not delete any files. |

```csharp
using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
using var request = new HttpRequestMessage(HttpMethod.Post,
    $"http://127.0.0.1:{builtInHttpPort}/api/royder-open-worktree");
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostToken);
request.Content = new StringContent(absoluteSolutionPath, Encoding.UTF8, "text/plain");
using var response = await client.SendAsync(request);
response.EnsureSuccessStatusCode();
var openedInstance = response.Headers.ETag.Tag; // Includes quotes. Send unchanged as If-Match on close/discard.
```

| HTTP status | Meaning |
|---|---|
| `200 OK` | Open or adoption completed (ETag), or Rider completed the selected close. Royder registration, indexing, Roslyn readiness and process exit are separate observations. |
| `400 Bad Request` | Invalid, relative or unsupported solution path. Open requires the physical path. |
| `403 Forbidden` | The token is unset or incorrect, the peer is non-loopback, or Rider refuses the request on trust grounds. |
| `404 Not Found` | Missing solution file for open, unknown route, or no matching candidate from the time of a close request. |
| `409 Conflict` | Ambiguous match, changed or unverifiable worktree, untrusted ancestor config, veto, unsaved documents or cancellation. A detected replacement returns the previous ETag. The plain-text body explains the refusal. |
| `412 Precondition Failed` | An open project exists at the path, but its instance tag differs. |
| `428 Precondition Required` | Discard was requested without an instance tag. |
| `500 Internal Server Error` | The handler logged an unexpected failure during project lookup or close. |

Opening uses `SolutionManager.openExistingSolution` in a new frame with ordinary restore and trust checks. It first
consults Rider's ancestor-configuration check. A config needing separate approval returns 409. For an authorized
solution it records the exact, solution-specific IntelliJ configuration location in `TrustedSolutionStore`, using
Rider's display-name and IDEA-name helpers. No trust-all switch or trusted parent worktree folder is added by the
plugin. After a successful open, Rider itself records the solution directory as a trusted *project path* in the
private profile. The supervisor remains responsible for authorizing each requested solution. Opening runs off the
HTTP thread and does not start another Java launcher.

The instance tag belongs to the live project, independent of whether the directory still exists. For managed saving
closes and repeat opens the plugin compares the physical solution directory and nearest Git marker with those
recorded at admission. Windows NIO returns a null file key here. The plugin reads Windows' volume serial and runs the
built-in `fsutil file queryfileid` without elevation, releasing each handle immediately. A source rewrite preserves
the registration. Replacing the directory or the Git marker changes its identity.

Both close operations snapshot the candidate projects when the request arrives. The event dispatch thread narrows
them to the still-open instance with that path and tag. Discard refuses if a dirty JVM document is open in or
belongs to the target, or if its owner cannot be established. It permits a document owned solely by a sibling.

Drain outstanding backend edits **before either close**. The plugin sees JVM documents. When the primary's own backend is the target, acknowledge the management
operation to its caller before dispatching the POST. That backend can exit before receiving the JVM's response.
The supervisor can confirm the old backend PID exited and the sibling solution still answers. After a lost reply,
check the worktree's open instance before issuing a new POST. A successful HTTP response alone does not establish
that Roslyn has released the worktree directory.

## Background saves and private-server specifications

In the pinned Rider, the no-save close suspends autosave while closing and skips both `saveAllDocuments` and
`saveSettingsUnderModalProgress(project)`. An independent IDE save can already have started before the supervisor
observes removal or before close begins. The save notification offers no per-project veto for all writers. Hence the
managed sequence remains close, observe backend/worker exit, then remove. Discard addresses the late close-time save;
it cannot undo or exclude earlier writes. The plugin never cleans up a reused directory. [Round 9 measurements](../../dev_docs/headless-royder/rider-remote-dev-server.md#round-9--two-worktrees-in-one-rider-jvm-22-november-2026)

With an already running private agent host containing both plugins, an indexed sibling solution and the management
token in the test process environment, run the black-box fixture:

```powershell
python -B experiments/RemoteCloseWorktree/Check.py --port $builtInHttpPort --jvm-pid $privateJvmPid --royder-port $privateRoyderPort --anchor $firstSolution
```

It checks the built-in port's owning PID before using the token. It creates a small local Git repository and linked
worktrees under a unique temp root, checks concurrent opens and instance-tag refusals, and exercises ordinary close,
disappearance and replacement. It compares the replacement's `.idea` contents and timestamps, checks a stale
discard after reopening, and verifies the original sibling still serves. It leaves the seed repository for inspection.
The command needs the Roslyn working-directory patcher for first-try removal while the project is still open.
