# Roslyn's working directory on a private Rider server

On Windows a running process keeps its current directory open. Rider starts the local Roslyn worker in the directory containing the solution. This prevents Git from removing a worktree even after Git has deleted its contents. This experiment gives the worker a directory owned by the agent server. Rider still selects the runtime and passes its original arguments and environment to Roslyn.

## Build

From the repository root on Windows, with the .NET 10 targeting pack and Rider **RD-263.7307.325** installed, run:

```powershell
dotnet build experiments/RemoteRoslynPatcher/RemoteRoslynPatcher.csproj -c Release --disable-build-servers
```

The project reads assemblies from `%LOCALAPPDATA%\Programs\Rider\lib\ReSharperHost`. Pass `-p:RiderHome=<installation directory>` for another copy of that build. The complete plugin folder is under the experiment's `bin/Release/net10.0/plugin/`:

```text
RemoteRoslynPatcher/
  lib/RemoteRoslynPatcher.jar           (contains META-INF/plugin.xml)
  dotnet/Royder.RemoteRoslynPatcher.dll
```

## Run in the next private-server test

Use a fresh agent server with the [round 7 isolation and memory guard](../../dev_docs/headless-royder/rider-remote-dev-server.md). Its launcher must already point Rider's config, system, plugins and log paths at the private state, copy Royder there and use a separate tools folder. Use the following additions **before the first JVM starts**. Run the staging commands from the repository root. Here `$lab` is the new private state directory used by that launcher, outside the worktree.

```powershell
$package = Resolve-Path 'experiments/RemoteRoslynPatcher/bin/Release/net10.0/plugin/RemoteRoslynPatcher'
$privatePlugins = Join-Path $lab 'plugins'
$destination = Join-Path $privatePlugins 'RemoteRoslynPatcher'
if (Test-Path -LiteralPath $destination) { throw 'Use a fresh private plugins folder.' }
Copy-Item -LiteralPath $package.Path -Destination $destination -Recurse
$env:ROYDER_REMOTE_ROSLYN_WORKING_DIRECTORY = Join-Path $lab 'RoslynWorker'
```

Pass the variable in the **server child's environment** when the launcher builds a separate environment block. The patcher creates the named directory on its first matching launch. With the variable unset the patcher delegates every request directly to Rider. The full solution backend is selected by `DaemonZone`. A local executable match uses the same assembly-directory calculation as `RoslynExternalProcess.GetWorkerPath` before Rider wraps the worker in `dotnet.exe`.

In that private launcher use the following argument list for `$riderHome\bin\remote-dev-server.exe`. Set `$solution` to the disposable worktree's absolute solution path. Keep the round 7 port check, child-process job and memory guard active.

```powershell
$runArguments = @(
  'run', $solution, '--listenOn', '127.0.0.1', '--port', '24931',
  '--ssh-link-host', '127.0.0.1', '--ssh-link-user', $env:USERNAME, '--ssh-link-port', '22'
)
```

Look for `RemoteRoslynProcessStartInfoPatcher selected Roslyn working directory` in the private backend log, then read the **live Roslyn worker PID's** Windows current directory. Check generator usages and an analyzer diagnostic, and try plain `git worktree remove` on a clean disposable tree with the worker alive. Record Git's first exit code, the worktree registration, and whether the path exists immediately after Git and again after `remote-dev-server.exe stop` from the same private environment. The project-store writes are a separate part of this test.

Round 8 verified compilation and package contents. Runtime component selection, the observed worker directory and the removal result await that test.
