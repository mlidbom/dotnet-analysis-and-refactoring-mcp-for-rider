# Close one worktree in a shared Rider server

This JVM plugin lets Royder's primary ask an agent server to save and close one open Rider project by its absolute
solution filename. It uses IntelliJ's existing built-in HTTP server. Install it only in the agent server's private
plugins directory. It has its own plugin ID and JAR and can be staged alongside the [Roslyn patcher](../RemoteRoslynPatcher/README.md).

## Build and verify

Run from the repository root in PowerShell 7 on Windows.

```powershell
./experiments/RemoteCloseWorktree/Build.ps1 -Verify
```

The script checks for **RD-263.7307.325** at `%LOCALAPPDATA%\Programs\Rider`. Pass `-RiderHome` for another copy of
that build. Its bundled Java compiler reads Rider's libraries and produces Java 21 bytecode. The installable folder
is `experiments/RemoteCloseWorktree/bin/plugin/RemoteCloseWorktree/` with a single `lib/RemoteCloseWorktree.jar`.

Omit `-Verify` for compilation and packaging alone. Verification uses the locally cached Plugin Verifier CLI 1.411
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
`ROYDER_MANAGED_HOST_TOKEN` disables all close requests. The handler also requires a loopback peer and Rider's
ordinary built-in HTTP trust checks. Keep the token in the management processes.

## Call from the primary

Send `POST /api/royder-close-worktree` to the **built-in HTTP port of the target host JVM**. The worktree supervisor
can set `-Drpc.port` in that JVM's options and verify the listening socket belongs to its PID before passing the port
to the primary. Rider can fall back to another port. Its JVM reports the actual port through
`BuiltInServerManager.getInstance().waitForStart().getPort()`. Royder's MCP port and the remote-dev listener serve
other protocols.

After authorizing the caller for the target worktree, use the exact absolute solution filename in that worktree's
registration. Send the path as UTF-8 plain text without quotes, BOM or trailing newline. Two worktrees may have
identically named solutions. The following C# shows the request with a dedicated management client.

```csharp
using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
using var request = new HttpRequestMessage(HttpMethod.Post,
    $"http://127.0.0.1:{builtInHttpPort}/api/royder-close-worktree");
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", hostToken);
request.Content = new StringContent(absoluteSolutionPath, Encoding.UTF8, "text/plain");
using var response = await client.SendAsync(request);
```

| HTTP status | Meaning |
|---|---|
| `200 OK` | `ProjectManager.closeAndDispose(project)` returned true. The response follows the close call. |
| `400 Bad Request` | The body has an invalid or relative path, or the request names an unknown operation under the service prefix. |
| `403 Forbidden` | The token is unset or incorrect, the peer is non-loopback, or Rider refuses the request on trust grounds. |
| `404 Not Found` | No matching file-backed project from the time of the request is still open. |
| `409 Conflict` | More than one project matches, Rider declined the close (including a veto), or it cancelled the operation. |
| `500 Internal Server Error` | The handler logged an unexpected failure during project lookup or close. |

Only POST is registered. The handler snapshots open project instances when it receives the request. On the event
dispatch thread it checks which are still open, matches the normalized Rider solution path, and calls the public
save-and-dispose API once. A project reopened at the same path while that request was queued is a different instance.
A veto ends the request with `409`; the handler leaves the decision in place. A modal dialog can delay dispatch.

Drain outstanding edits before closing. When the primary's own backend is the target, acknowledge the management
operation to its caller before dispatching the POST. That backend can exit before receiving the JVM's response.
The supervisor can confirm the old backend PID exited and the sibling solution still answers. After a lost reply,
check the worktree's open instance before issuing a new POST. A successful HTTP response alone does not establish
that Roslyn has released the worktree directory.

## Verification in this round

The standalone JAR compiled against **RD-263.7307.325**, and offline Plugin Verifier **1.411** reported **Compatible**.
The verifier logged missing optional components of the local Rider installation and reported no plugin compatibility
findings. No Rider, remote-dev server or HTTP client was run. The two-worktree close and removal check, including veto
behavior in a running host, remains for the live round. [Installed-code study](../../dev_docs/headless-royder/remote-dev-server-closing-one-project.md)
