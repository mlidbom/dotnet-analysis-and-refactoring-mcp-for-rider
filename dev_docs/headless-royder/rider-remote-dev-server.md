# Rider Remote Development as a host for Royder

**Verdict, 22 November 2026, after the run of round 6** ([below](#round-6--observed-on-magnuss-machine-22-november-2026)). **A Rider remote-development host that no client ever connected to runs Royder with the editing zone.** Its backends ran `ICodeEditingZone`, and Royder's dry-run `rename_symbol` gave the right change. Several solutions share one host JVM, each with a full backend of its own. The first small solution took about 3.3 GiB, the JVM included, and a second about 0.65 GiB more. One `stop` ends them all.

- **Not yet tested:** a committed rename, completion, a quick fix, and a host that runs for hours.
- **Not contained:** the host writes status files to the real `%LOCALAPPDATA%\JetBrains\RemoteDev` despite the four IDE folder overrides, and it touched the timestamps of `PermanentUserId` and `PermanentDeviceId`.
- **JetBrains' terms:** they don't say whether this use is covered. Magnus judged on 2026-11-19 that his subscription covers our own private use, and the terms get checked before anything is published.

The research before the run follows as written then. Its verdict was: "I could not verify a licensed route for a never-connected agent to perform refactorings through Royder." [Rider remote-development licensing](https://www.jetbrains.com/help/rider/Licensing_and_useful_links.html) · [Working in a connected remote project](https://www.jetbrains.com/help/rider/Work_inside_remote_project.html)

### Which installation Magnus is running

**Verified from executable paths.** The real desktop `rider64.exe` processes in the initial snapshot, including PID 74436, came from `C:\Users\magnu\AppData\Local\Programs\Rider\bin\rider64.exe`. Their product installation has `product-info.json`, `bin\remote-dev-server.exe` and the bundled plugins. It reports **Rider 2026.3.0.1, RD-263.7307.325**. The running `Rider.BackendHost.exe` and `Rider.LightweightBackend.exe` images came from `C:\Program Files\JetBrains\Rider\r2r\2026.3.0R\79EA988132964F73F9D53D204151700\windows-x64\`. At inspection, the Program Files root contained only `r2r`. Use the per-user installation to locate the remote launcher. Executables named `rider64.exe` under a temporary specifications directory were excluded by path.

In the file references below, **Rider home** means `C:\Users\magnu\AppData\Local\Programs\Rider`. **Verified** means documented by JetBrains or present in the installed build. **Inferred** means expected behavior pending a run; it is not a licensing conclusion.

## 1. Commands, the .NET backend and plugins

**Verified.** `bin/remote-dev-server.exe` is the Windows launcher. The installed `plugins/remote-dev-server/lib/remote-dev-server.jar!/META-INF/plugin.xml` describes an unattended IDE host to which a client can connect. Its command implementation is in `plugins/remdev-plugin/lib/modules/intellij.platform.remoteController.backend.jar` and the companion CLI module. JetBrains documents a manual server-first launch followed by a separate client connection. For Rider the FAQ specifically requests the full solution file path. The Gateway manual examples use Unix `.sh` syntax. JetBrains lists Windows remote hosts for its Toolbox/Rider connection flows and Linux for Gateway’s SSH deployment flow. Treat the Windows executable inspected here as a distinct launch route. [Manual server-first launch](https://www.jetbrains.com/help/rider/Remote_development-a.html) · [Rider Remote Development FAQ](https://www.jetbrains.com/help/rider/FAQ_about_remote_development.html) · [Supported connection methods](https://www.jetbrains.com/help/rider/Remote_development_overview.html)

| Command found in the Windows distribution | Purpose and evidence |
|---|---|
| `run <solution> [options]` | Launch or address the remote host and open the specified project. The host starter is `remoteDevHost`. `UnattendedHostCliHelper` defines `--listenOn`/`-l` and `--port`/`-p`; SSH-link options describe the connection link, not Royder’s listener. |
| `status [project]`, `stop` | Query remote-host status; request exit of the addressed IDE instance. The packaged shell launcher translates these to `remoteDevStatus` and `exit`. Scope a stop to an isolated instance when several solutions may share it. |
| `warm-up <solution>` | Run the separate `warmup` starter to prepare project caches. The packaged launcher passes `--project-dir=<path>`. `lib/intellij.platform.warmup.jar!/intellij.platform.warmup.xml` registers `ProjectCachesWarmup`. Rider’s own `intellij.rider.xml` registers `RiderProjectIndexesWarmupSupport`, which waits on `SolutionLifecycleHost.isBackendLoaded`. This is a preparation job; use `run` for a resident MCP peer. |
| `installPlugins`, `registerBackendLocationForGateway` | Install plugins for a target remote project; register an existing IDE installation so Gateway can find it. Both involve writes. JetBrains documents remote plugin installation and Gateway registration separately. [Remote plugin installation](https://www.jetbrains.com/help/rider/Work_inside_remote_project.html) · [Gateway registration](https://www.jetbrains.com/help/rider/Remote_development-a.html) |
| `invalidate-caches`, `dump-launch-parameters`, `print-env-var`, `provisionTbeBackend` and help | The Windows executable includes these command names and the corresponding internal spellings for cache invalidation, launch parameters and environment diagnostics. Provisioning is an IDE Services/Toolbox backend operation. Help and internal starter aliases are distinct from the project-serving command. Presence of these names is verified; their Windows argument contracts were not exercised. |

The shipped POSIX companion `plugins/remote-dev-server/bin/launcher.sh:13–31,192–228` exposes the basic usage and mappings. The native Windows executable contains its own help strings. I read them as data and did not execute either launcher, including help mode.

**Verified implementation; inferred outcome for Royder.** Rider home’s `product-info.json` sets `com.jetbrains.rider.protocol.EarlyBackendStarter` as early JVM startup code. `lib/intellij.rider.rdclient.dotnet.startup.jar` contains that starter and `RiderBackendLauncher`; the warm-up extension above explicitly waits for the .NET solution backend. Remote Development supports plugins installed on the host. Magnus’s installed Royder is `%APPDATA%/JetBrains/Rider2026.3/plugins/DotNetAnalysisAndRefactoringMcp`: its version 0.15.1 JAR descriptor requires `com.intellij.modules.rider`; it includes `dotnet/DotNetAnalysisAndRefactoringMcp.dll` and a bundled tools build with `host.sha256`. Together these make loading **unchanged Royder in the real .NET backend plausible**. Discovery, component construction, port binding and working tools with zero attached clients remain unverified. A JVM listener or join link alone would prove none of them. [JetBrains split-mode plugin architecture](https://plugins.jetbrains.com/docs/intellij/split-mode-and-remote-development.html)

## 2. Licensing and the editing zone

**Verified.** In the documented remote-development flow the connecting machine checks a matching **Rider** entitlement. The backend may start before license information is entered. JetBrains says this flow does not send or store license information on the remote server; any floating license server must be reachable from the client. A host-side JetBrains Account sign-in, activation code or copied license file is not a prerequisite for that startup. For individual users JetBrains describes an active subscription to the remote IDE; it distinguishes corporate orchestration arrangements. I did not inspect Magnus’s license, infer his contract category, or contact an activation service. [Rider licensing](https://www.jetbrains.com/help/rider/Licensing_and_useful_links.html) · [JetBrains Remote Development subscription FAQ](https://www.jetbrains.com/remote-development/)

**Unresolved for agent editing.** JetBrains has explicitly given automated server/image warm-up as an example of unattended preparation. That explanation predates Rider’s Remote Development support. Neither it nor the current Rider documentation says a plugin may offer arbitrary licensed refactorings to agents indefinitely before the first client connects. Likewise, the fact that editing is supported through a licensed client does not establish that a never-connected .NET backend activates `ICodeEditingZone` or that the entitlement persists after disconnect. **A run can show technical availability; JetBrains or the applicable agreement must settle permission.** The concrete question for JetBrains is whether Magnus’s subscription covers autonomous MCP edits in his own worktrees on an unattended Rider host, with no client ever validating the session, or whether it requires an attached authenticated client and/or an orchestration arrangement. [JetBrains’ warm-up explanation](https://blog.jetbrains.com/blog/2021/12/03/dive-into-jetbrains-gateway/) · [Current Rider licensing](https://www.jetbrains.com/help/rider/Licensing_and_useful_links.html)

## 3. One project per connection; possibly several per host JVM

**Verified in the installed host.** `run` takes one project argument. `UnattendedHostStarter.canProcessExternalCommandLine()` returns true. Its external-command handler accepts another project path. `UnattendedModeManagerImpl.Companion.prepareProject` enumerates `ProjectManager.getOpenProjects()`, checks for the requested path and can open another project via `ProjectUtil.openOrImportAsync` with an `OpenProjectTask`. In `lib/intellij.remoteDev.util.jar`, `UnattendedHostStatus` contains **one `appPid` and a list of projects**; the per-project records have their own path, connection state and background-task state. This is actual multi-project host code. The documentation’s statement about reusing one *JetBrains Client* process describes the client side. A new remote project requires a project-specific connection. [Project-bound connections](https://www.jetbrains.com/help/rider/Remote_development-a.html) · [Multiple client connections](https://www.jetbrains.com/help/rider/Work_inside_remote_project.html)

**Inferred.** A remote host JVM can manage several projects; there is no inherent one-JVM-per-project requirement in this starter. Whether two Windows launcher invocations with the same profile reuse that JVM in this build, whether each solution gets its own full `Rider.BackendHost.exe`, and how reliably that isolates Royder’s peers require a two-solution observation. Separate config/system roots deliberately favor separate IDE instances and would test a different topology.

## 4. Memory

**Verified configuration, unknown footprint.** Rider home’s `bin/rider64.exe.vmoptions` specifies `-Xms128m`, `-Xmx2048m` and a 512 MiB reserved code cache. The native launcher loads a JBR JVM; even `UnattendedHostStarter.isHeadless()` returns false as an IntelliJ application-starter flag. An unattended server still carries IntelliJ Platform infrastructure. Heap and code-cache settings are not a measurement of committed memory or working set. The host additionally needs ReSharper, potentially its lightweight backend and project workers. JetBrains’ Linux SSH guidance recommends 4 GB or more for the **remote machine**, not for the JVM alone. I found no version-matched published JVM-only steady-state or startup-peak figure for this Rider build. I measured no memory. [Remote-host requirements](https://www.jetbrains.com/help/rider/Prerequisites.html) · [Rider remote JVM tuning](https://www.jetbrains.com/help/rider/Work_inside_remote_project.html)

## 5. Isolating config, system, plugins and logs

**Verified controls.** Rider home’s `bin/idea.properties:8–23` and the Rider directory guide name `idea.config.path`, `idea.system.path`, `idea.plugins.path` and `idea.log.path`. Give them four distinct absolute paths; use forward slashes in the properties file on Windows. JetBrains documents `RIDER_PROPERTIES` for a custom properties file and `RIDER_VM_OPTIONS` for a private copy of the default VM options. Preserve the shipped VM options when adding explicit `-Didea.*.path=…` values. The POSIX companion has `IJ_HOST_CONFIG_DIR` and `IJ_HOST_SYSTEM_DIR` handling, but those names were absent from the Windows native executable’s strings. The Windows proposal uses Rider’s documented properties instead. [Rider directory overrides](https://www.jetbrains.com/help/rider/Directories_Used_by_the_IDE_to_Store_Settings_Caches_Plugins_and_Logs.html) · [Product properties environment variables](https://intellij-support.jetbrains.com/hc/en-us/articles/207240985-Changing-IDE-default-directories-used-for-config-plugins-and-caches-storage) · [VM option precedence](https://www.jetbrains.com/help/rider/Tuning_the_IDE.html)

**Limit.** The four properties address IntelliJ directories, not every child/native/user setting. Rider’s installed `lib/ReSharperHost/JetBrains.Platform.Core.dll` contains the .NET-side `JET_LOCAL_APPDATA_DIR`, `JET_ROAMING_APPDATA_DIR`, `JET_TEMP_DIR` and `RESHARPER_HOST_LOG_DIR` controls. Give Royder its own tools folder and port, and use private process temp, CLI home and NuGet directories. None of that proves that all plugins, known-folder APIs, settings import, credentials or project evaluation avoid the current Windows profile. **For a hard zero-write guarantee for Magnus’s folders, use a separate disposable Windows VM/user profile without those folders mounted writable.** A later same-user run would require separate write-footprint verification; changing `APPDATA` strings alone is insufficient.

## 6. Network contacts and controls

**Verified possibilities; unmeasured on this launcher.** The following are documented services or installed defaults. There is no observed list of requests from an isolated no-client `run` in this research. The executable’s embedded property strings and `launcher.sh:488–501` cover platform updates and shared indexes. Localhost traffic between the JVM, .NET backend and workers is separate from Internet egress.

| Purpose | Potential contact | Control for an isolated host |
|---|---|---|
| Licensing | Normal matching entitlement is checked on client connection. Depending on license type, the client may need `account.jetbrains.com`, `oauth.account.jetbrains.com` or the organization’s license server. Rider also documents desktop duplicate-license checks; their occurrence in this server mode is unknown. | For a startup-only observation, leave the client disconnected and provision no host credentials. For licensed editing, use ordinary client activation or an offline option JetBrains supports for the license concerned. There is no documented edit-enabled switch for turning off license validation. [Remote licensing](https://www.jetbrains.com/help/rider/Licensing_and_useful_links.html) · [Rider network activity](https://www.jetbrains.com/help/rider/Introduction__Network_Activity.html) |
| Platform and .NET product updates | `www.jetbrains.com` update/config endpoints and distribution CDNs. Remote Dev’s launcher includes `ide.no.platform.update`. The installed `JetBrains.Platform.Shell.dll!UpdatesManager` contains the separate .NET update check and `JETBRAINS_NO_CHECK_FOR_UPDATES`. | Set `-Dide.no.platform.update=true` in the private VM options, disable IDE/plugin update checks in private settings and set `JETBRAINS_NO_CHECK_FOR_UPDATES=1` for the child. These controls have different scopes. [JetBrains support on update checks](https://support.jetbrains.com/hc/en-us/articles/40253031766930-How-to-disable-auto-update-check-for-IDE) |
| Marketplace | `plugins.jetbrains.com` for search, updates and suggestions; `downloads.marketplace.jetbrains.com` for plugin files. | Prestage Royder from disk. Disable **Check for plugin updates** and **Update plugins automatically**; avoid `installPlugins` and Marketplace search. Where a repository is needed, configure an internal repository. Disabling automatic updates does not establish silence from recommendations. `lib/intellij.platform.ide.impl.jar!UpdateOptions` includes `PLUGINS_CHECK_NEEDED` and `PLUGINS_AUTO_UPDATE`. [Rider updates](https://www.jetbrains.com/help/rider/Update.html) · [Plugin sources](https://www.jetbrains.com/help/rider/Managing_Plugins.html) |
| Statistics and optional services | `resources.jetbrains.com` for statistics configuration; other feature/configuration domains may be used. Optional AI, Backup and Sync, symbol download and project feeds have their own destinations. | For licenses with optional reporting, turn off anonymous usage statistics in the private profile. `lib/intellij.platform.statistics.jar!StatisticsUploadAssistant.isSendAllowed` also checks `idea.suppress.statistics.report`. Do not assume this controls configuration fetching or all plugins. Non-commercial licenses have mandatory consent. Leave optional integrations disabled, use local dependency sources, and use a disconnected guest if zero external traffic is a requirement. [Rider data sharing](https://www.jetbrains.com/help/rider/Settings_Usage_Statistics.html) · [JetBrains endpoint inventory](https://support.jetbrains.com/hc/en-us/articles/40250675463570-Firewall-or-proxy-allowlist-for-IntelliJ-based-IDEs-required-hosts-and-ports) |
| Remote client, runtime and indexes | Gateway/Toolbox may fetch matching Client/JBR/distribution artifacts from `download.jetbrains.com`, `download-cdn.jetbrains.com`, `cache-redirector.jetbrains.com` and `data.services.jetbrains.com/products`. The companion launcher enables shared-index downloads by default. | A preinstalled backend needs no deployment download; an unconnected run needs no client download. Prestage matching client/runtime artifacts for a later air-gapped connected test. Disable shared-index downloads in the private configuration and verify effective values; a launcher may inject its own defaults. A generated HTTPS join link is text, not evidence the backend contacted that URL. [Rider Remote Development FAQ](https://www.jetbrains.com/help/rider/FAQ_about_remote_development.html) · [Supported offline setup](https://www.jetbrains.com/help/rider/Fully_offline_mode.html) |

## 7. Can it start work and remain available without a client?

**Verified startup path.** The `remoteDevHost` starter initializes the direct connection listener, prints its link, starts status renewal and calls `UnattendedModeManagerImpl.openProject` on the normal path with a project argument. That path reaches `ProjectUtil.openOrImportAsync`; there is no intervening wait for a remote controller to attach. The installed status model explicitly represents a project with `controllerConnected=false`. JetBrains separately documents that closing a locally connected project does not automatically close it on the remote host. [Rider remote session lifecycle](https://www.jetbrains.com/help/rider/Work_inside_remote_project.html)

**Inferred.** With a valid, trusted, restored solution, one should be able to see a loaded/indexed .NET solution with no first client. Project trust, first-run prompts, incomplete restore, plugin compatibility, a host idle policy or the actual Rider zone composition could change the result. A printed join link is earlier than project readiness; post-disconnect survival does not establish indefinite operation in a never-connected session. The decisive observations are a live per-project status without a controller, the full .NET backend PID, and a Royder health row for the exact solution with `indexing=false` and a real tools build.

## Proposed run for Magnus’s decision only

**First decision: approve only the no-client startup/readiness observation.** Use a disposable, already-provisioned Windows VM with its external network disconnected, a fresh ordinary local OS account, the matching Rider build, an installed .NET 10 SDK/targeting packs, and no writable mapping of Magnus’s profile. This gives a real isolation boundary even if a directory override is ignored. Copy only the installed Royder **plugin directory** into the guest as `C:\RoyderInput\DotNetAnalysisAndRefactoringMcp`. Prestage Rider at `C:\RoyderInput\Rider`. Do not provide user settings, license material, Toolbox state, a client, or an account login. Use an empty snapshot with no other Rider. If those prerequisites are unavailable, defer this particular run.

The commands below are for PowerShell 7 **inside that guest**. They were not executed. Ports 24931 and 24941 serve different protocols. Require the whole Royder peer-probe range to be free so the test cannot attach to another host.

~~~powershell
$ide = 'C:\RoyderInput\Rider'
$plugin = 'C:\RoyderInput\DotNetAnalysisAndRefactoringMcp'
$lab = 'C:\RoyderLab\RemoteDev01'
if (Test-Path -LiteralPath $lab) { throw 'Use a fresh lab directory.' }
$requiredFree = @(24931) + @(24941..24950)
$busy = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
if ($busy | Where-Object { $_.Port -in $requiredFree }) { throw 'Test port already in use.' }
$dirs = 'config','system','plugins','log','temp','dotnet-local','dotnet-roaming',
        'tools','cli','packages','work','config/options'
foreach ($d in $dirs) { [void][IO.Directory]::CreateDirectory((Join-Path $lab $d)) }
$forward = $lab.Replace('\','/')
@(
  "idea.config.path=$forward/config"
  "idea.system.path=$forward/system"
  "idea.plugins.path=$forward/plugins"
  "idea.log.path=$forward/log"
  'shared.indexes.download=false'
  'shared.indexes.download.auto.consent=false'
) | Set-Content "$lab\idea.properties" -Encoding utf8NoBOM
Copy-Item -LiteralPath "$ide\bin\rider64.exe.vmoptions" -Destination "$lab\rider.vmoptions"
@(
  ''
  "-Didea.config.path=$forward/config"
  "-Didea.system.path=$forward/system"
  "-Didea.plugins.path=$forward/plugins"
  "-Didea.log.path=$forward/log"
  "-Djava.io.tmpdir=$forward/temp"
  '-Dide.no.platform.update=true'
) | Add-Content "$lab\rider.vmoptions" -Encoding utf8NoBOM
'<application><component name="UpdatesConfigurable"><option name="CHECK_NEEDED" value="false"/><option name="PLUGINS_CHECK_NEEDED" value="false"/><option name="PLUGINS_AUTO_UPDATE" value="false"/></component></application>' |
  Set-Content "$lab\config\options\updates.xml" -Encoding utf8NoBOM
$env:RIDER_PROPERTIES = "$lab\idea.properties"
$env:RIDER_VM_OPTIONS = "$lab\rider.vmoptions"
$env:JET_LOCAL_APPDATA_DIR = "$lab\dotnet-local"
$env:JET_ROAMING_APPDATA_DIR = "$lab\dotnet-roaming"
$env:RESHARPER_HOST_LOG_DIR = "$lab\log"
$env:JET_TEMP_DIR = "$lab\temp"
$env:TEMP = "$lab\temp"
$env:TMP = $env:TEMP
$env:DOTNET_CLI_HOME = "$lab\cli"
$env:NUGET_PACKAGES = "$lab\packages"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
$env:JETBRAINS_NO_CHECK_FOR_UPDATES = '1'
$env:DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT = '24941'
$env:DOTNET_ANALYSIS_AND_REFACTORING_MCP_TOOLS = "$lab\tools"
Copy-Item -LiteralPath $plugin -Destination "$lab\plugins\DotNetAnalysisAndRefactoringMcp" -Recurse
# The empty private development-tools directory leaves the matching build shipped inside the plugin available.
Set-Location "$lab\work"
dotnet new sln -n Probe --format sln
if ($LASTEXITCODE) { throw 'Fixture solution creation failed.' }
dotnet new classlib -n Shapes -f net10.0 --no-restore
if ($LASTEXITCODE) { throw 'Fixture project creation failed.' }
'namespace Shapes; public static class ProbeShape { public static int Twice(int x) => x + x; public static int Use() => Twice(2); }' |
  Set-Content '.\Shapes\ProbeShape.cs' -Encoding utf8NoBOM
dotnet sln Probe.sln add '.\Shapes\Shapes.csproj'
if ($LASTEXITCODE) { throw 'Adding fixture project failed.' }
'<configuration><packageSources><clear/></packageSources></configuration>' |
  Set-Content "$lab\NuGet.Config" -Encoding utf8NoBOM
dotnet restore Probe.sln --configfile "$lab\NuGet.Config" -p:NuGetAudit=false
if ($LASTEXITCODE) { throw 'Offline fixture restore failed.' }

$sln = (Resolve-Path 'Probe.sln').Path
$remote = "$ide\bin\remote-dev-server.exe"
$hostProcess = Start-Process -FilePath $remote -WorkingDirectory $lab -WindowStyle Hidden -PassThru -ArgumentList @(
  'run',$sln,'--listenOn','127.0.0.1','--port','24931',
  '--ssh-link-host','127.0.0.1','--ssh-link-user',$env:USERNAME,'--ssh-link-port','22'
) -RedirectStandardOutput "$lab\host.stdout" -RedirectStandardError "$lab\host.stderr"
~~~

**Observe phase A without a client.** Run the following in the same private shell. Allow up to two minutes. Separately inspect the guest’s JVM/Rider logs for actual effective directories, plugin discovery, backend startup, project trust or activation prompts. The `status` output and host logs can contain a bearer join link: keep them in the guest. Verify the listener ownership from process paths. Keep the host running through a short observation interval with `controllerConnected=false`. If requested by Magnus, record per-process private bytes and working set **in the future run** for the JVM host, full and lightweight .NET backends, and child workers separately. A sum of working sets can count shared pages twice.

~~~powershell
$until = [DateTime]::UtcNow.AddMinutes(2)
$loaded = $null
do {
  try {
    $health = Invoke-RestMethod 'http://127.0.0.1:24941/health' -NoProxy -TimeoutSec 2
    $loaded = @($health.solutions | Where-Object { $_.path -eq $sln -and $_.indexing -eq $false -and $_.tools.build })
  } catch { $loaded = $null }
  if (-not $loaded) { Start-Sleep -Seconds 1 }
} until ($loaded -or [DateTime]::UtcNow -ge $until)
& $remote status $sln *> "$lab\remote-status-private.txt"
Get-CimInstance Win32_Process |
  Where-Object Name -in 'remote-dev-server.exe','rider64.exe','Rider.BackendHost.exe','Rider.LightweightBackend.exe' |
  Select-Object ProcessId,ParentProcessId,Name,ExecutablePath
if ($loaded) { $loaded | Select-Object name,path,indexing,@{n='toolsBuild';e={$_.tools.build}} }
~~~

A success requires the real `Probe.sln` row, a live full `Rider.BackendHost.exe`, an actual Royder tools build, and no connected controller. A failure should record which boundary stopped progress: no JVM listener, trust/first-run prompt, no full .NET backend, backend without Royder, or an indexed solution lacking editing services. On a prompt requiring sign-in or activation, stop and record it. Do not interpret an early link or an empty health response as success.

**Optional topology control.** With the first host still running, issue a second launch under the *same* private settings. These commands create a second traditional solution without copying the first project's restored intermediates. Record both project paths, the host `appPid` and all full .NET backend PIDs. If the second launch reports a port collision, that result alone does not establish the process model.

~~~powershell
$other = "$lab\other"
[void][IO.Directory]::CreateDirectory($other)
Set-Location $other
dotnet new sln -n Second --format sln
if ($LASTEXITCODE) { throw 'Second solution creation failed.' }
[void][IO.Directory]::CreateDirectory("$other\Shapes")
Copy-Item -LiteralPath "$lab\work\Shapes\Shapes.csproj","$lab\work\Shapes\ProbeShape.cs" -Destination "$other\Shapes"
dotnet sln Second.sln add '.\Shapes\Shapes.csproj'
if ($LASTEXITCODE) { throw 'Adding second fixture project failed.' }
dotnet restore Second.sln --configfile "$lab\NuGet.Config" -p:NuGetAudit=false
if ($LASTEXITCODE) { throw 'Second offline restore failed.' }
$secondSln = (Resolve-Path 'Second.sln').Path
$secondLaunch = Start-Process -FilePath $remote -WorkingDirectory $lab -WindowStyle Hidden -PassThru -ArgumentList @(
  'run',$secondSln,'--listenOn','127.0.0.1','--port','24931',
  '--ssh-link-host','127.0.0.1','--ssh-link-user',$env:USERNAME,'--ssh-link-port','22'
) -RedirectStandardOutput "$lab\second.stdout" -RedirectStandardError "$lab\second.stderr"
& $remote status $secondSln *> "$lab\second-status-private.txt"
~~~

**Optional licensed editing control.** If Magnus approves the documented connected control, use the matching JetBrains Client and ordinary license validation. Record the before-connection, connected and after-disconnection states separately. An offline guest with no credentials does not guarantee a successful online client check. A never-connected **editing** trial needs an answer from JetBrains or the applicable agreement covering that use. The following command proposes a non-writing refactoring test for the authorized configuration:

~~~powershell
# Only in the separately approved, licensed editing phase.
$body = @{
  jsonrpc='2.0'; id=1; method='tools/call'
  params=@{name='rename_symbol';arguments=@{
    solutionName=$sln; symbolName='Shapes.ProbeShape.Twice'; newName='TwiceForProbe'; dryRun=$true
  }}
} | ConvertTo-Json -Depth 8
Invoke-RestMethod 'http://127.0.0.1:24941/' -NoProxy -Method Post -ContentType 'application/json' -Body $body
~~~

Require the refactoring result to identify the declaration and call site and leave the fixture unchanged. `tools/list` advertising `rename_symbol` is insufficient. Test `complete_at` and a specific available bulb separately before promising those tools: they also need usable editor services. For shutdown in the isolated guest, run `& $remote stop $sln` from the same private environment, verify the owned JVM/backend PIDs and both listeners go away, and discard the VM snapshot if graceful stop fails. Nothing in the proposed experiment changes `ZoneLicenseChecker` or the license configuration.

**Expected communications.** Phase A addresses only the guest’s loopback Remote Development listener at 24931, Royder HTTP at 24941, and dynamically assigned local JVM/.NET/worker IPC. With the guest disconnected it cannot complete any Internet or license-server exchange; record attempted connections separately from successful ones. Supplying local SSH-link fields generates metadata and does not itself deploy an SSH session. A separately authorized connected control adds client↔host RD/SSH as applicable and normal client-side entitlement traffic, plus any missing client/runtime downloads and enabled network features listed in section 6. The exact destinations of either real run remain observations to make, not findings from this round.

No IDE, project, warm-up, help mode, license check or benchmark was started during this research. Only public documentation, installed files, and executable paths of existing processes were read.

## Round 6 — observed on Magnus’s machine, 22 November 2026

**The never-connected remote Rider host performed Royder’s semantic rename preview.** Both full backend logs list `ICodeEditingZone` in `ZonesToRun`. A second solution opened in the **same host JVM**, with a second full `Rider.BackendHost.exe`. No `Rider.LightweightBackend.exe` or JetBrains Client appeared in the experiment tree. The indexed tree used **3,292.2 MiB private for A** and **3,950.0 MiB for A+B**, an observed increment of **657.8 MiB** for the second small solution. This was a real, online run of the stock product with Magnus’s license available. No license file was copied, sign-in performed, license check altered, or source edit applied.

### What ran

I used the installed `%LOCALAPPDATA%\Programs\Rider\bin\remote-dev-server.exe` (RD-263.7307.325) with two independently restored, one-project `net10.0` solutions outside any checkout: `C:\Dev\RoyderRemoteRound6\A\Probe.sln` and `...\B\Probe.sln`. Both contained a declaration of `ProbeShape.Twice` and exactly one call, from `CallTwice`. All times below are local.

The private state root was `%TEMP%\royder-headless\codex-1-round-6`. I used `RIDER_PROPERTIES` and `RIDER_VM_OPTIONS` with distinct private `idea.config.path`, `idea.system.path`, `idea.plugins.path` and `idea.log.path`; the launcher printed the expected config, system and log directories. I copied the installed Royder plugin read-only from Magnus’s profile into the private plugins directory. I redirected ReSharper local/roaming/temp/log paths, JVM home/temp, process `APPDATA`/`LOCALAPPDATA`, .NET CLI home, NuGet caches and Royder’s development tools directory. The private profile disabled IDE/plugin update checks, automatic plugin updates, shared-index downloads and statistics reporting. `JETBRAINS_NO_CHECK_FOR_UPDATES=1` was set only in the experiment’s child environment. The trust entries covered only the two private probe folders’ parent.

The port check found 24931 and 24941–24950 free. At 04:53:16 I ran `remote-dev-server.exe run C:\Dev\RoyderRemoteRound6\A\Probe.sln --listenOn 127.0.0.1 --port 24931 --ssh-link-host 127.0.0.1 --ssh-link-user <current-user> --ssh-link-port 22`. At 04:53:50 I repeated it with B’s absolute solution path and the same environment and port. Each launcher was assigned to a private Windows job before its first instruction ran. The memory guard was active throughout: the two starts had **21.91 GiB** and **18.92 GiB** available; the observed minimum was **16.04 GiB**. The 2 GiB stop threshold was never approached.

### Phase A — no client and unchanged source

Royder returned the exact A path with `indexing=false` after **13.06 s**, `pendingDiskChanges=0`, host `7529fd2e44f63a15a6f0473b681767eea4fbb2591bbe74328433e4ec1d1d7660` and tools build `2026-11-19_11-59-23-7530405` from the *private* copied plugin. Remote Dev’s status reported `appPid=217732`, `controllerConnected=false` and no modal dialog. A’s backend log recorded the Royder **primary on 24941**. The sampler saw no lab-owned TCP connection to port 23751.

| Royder call | Answer |
|---|---|
| `find_usages` of `Shapes.ProbeShape.Twice` | One usage in one file and one project. Declaration at `Shapes\ProbeShape.cs:4`, call at line 5: `public static int CallTwice() => Twice(2);`. |
| `get_diagnostics` on `ProbeShape.cs` | `diagnosticsCount: 0` at default minimum severity warning. The daemon produced 29 highlightings, all below that threshold. |
| `rename_symbol` from `Twice` to `TwiceForProbe` with `dryRun: true` | MCP `isError: false`; the exact text payload follows. SHA-256 of the named source before and after was `83b3c8b2a5fb7fb983d3a499677254f87b085dd5e0b190714d63b4955b1aa5fa`. |

~~~json
{
  "applied": false,
  "dryRun": true,
  "oldName": "Twice",
  "newName": "TwiceForProbe",
  "conflicts": [],
  "changedFiles": [
    {
      "file": "C:\\Dev\\RoyderRemoteRound6\\A\\Shapes\\ProbeShape.cs",
      "fileName": "ProbeShape.cs"
    }
  ],
  "skipped": null
}
~~~

This proves that this Rider configuration ran the editing zone and the semantic rename preview without any client ever attaching. It does not test a committed rename, completion, a particular quick fix, or which mechanism the product used to recognize an entitlement.

### Phase B — topology and measured memory

The second `run` launcher (PID 110312) handed B to the existing JVM (PID **217732**). One remote status response listed **both absolute solution paths under that app PID**, both with `controllerConnected=false`. A retained full backend **39980**, Royder primary on **24941**. B got full backend **117224**, Royder peer on **24942** registering with **24941**. B became indexed in **9.61 s**; the same Royder health response then listed both indexed solutions with their actual host/build identifiers. There was no client connection in either phase.

**Per-process memory in MiB.** Figures are medians of private committed bytes and working set over the post-index observation intervals. A had 12 samples over 15.9 s, A+B had 10 over 17.9 s. No tool calls ran during those intervals. A’s first status still reported IDE background tasks, so these are post-index observations, not a claim that all caches had settled. A dash means that process was absent in that interval.

| Process | PID | A private | A working set | A+B private | A+B working set |
|---|---:|---:|---:|---:|---:|
| Remote Rider JVM, `remote-dev-server.exe` | 217732 | 1,842.7 | 2,147.6 | 1,862.0 | 2,167.5 |
| A full `Rider.BackendHost.exe` | 39980 | 1,227.7 | 1,559.2 | 1,104.7 | 1,454.4 |
| B full `Rider.BackendHost.exe` | 117224 | — | — | 559.3 | 911.9 |
| Lightweight .NET backend | — | — | — | — | — |
| A `dotnet.exe` hosting `MSBuild.dll` | 240544 | 38.9 | 102.8 | 37.6 | 101.6 |
| A `dotnet.exe` hosting the Roslyn worker | 138420 | 91.9 | 225.2 | 89.8 | 227.8 |
| A `JetBrains.DPA.Protocol.Backend.exe` | 249636 | 83.8 | 183.5 | 81.8 | 181.6 |
| B `dotnet.exe` hosting `MSBuild.dll` | 164640 | — | — | 39.3 | 102.0 |
| B `dotnet.exe` hosting the Roslyn worker | 131976 | — | — | 76.7 | 179.8 |
| B `JetBrains.DPA.Protocol.Backend.exe` | 34940 | — | — | 82.4 | 182.5 |
| `fsnotifier.exe` | 131784 | 1.0 | 4.8 | 0.9 | 4.7 |
| Console host of JVM | 71632 | 1.5 | 8.3 | 1.5 | 8.3 |
| Console host of file watcher | 114508 | 1.4 | 8.1 | 1.4 | 8.1 |
| Console host of A backend | 4280 | 1.5 | 8.3 | 1.5 | 8.3 |
| Console host of A MSBuild | 148452 | 1.5 | 8.3 | 1.5 | 8.3 |
| Console host of A Roslyn | 35788 | 1.5 | 8.3 | 1.5 | 8.3 |
| Console host of A DPA | 147352 | 1.5 | 8.3 | 1.5 | 8.3 |
| Console host of B backend | 22520 | — | — | 1.5 | 8.3 |
| Console host of B MSBuild | 253036 | — | — | 1.5 | 8.3 |
| Console host of B Roslyn | 150160 | — | — | 1.5 | 8.3 |
| Console host of B DPA | 141076 | — | — | 1.5 | 8.3 |
| **Whole job, median of per-sample totals** | | **3,292.2** | **4,240.9** | **3,950.0** | **5,601.0** |

The **657.8 MiB** net private increase is about **0.64 GiB**. B’s backend plus its own children accounted for **759.7 MiB private**. The JVM grew by **19.3 MiB** while A’s existing full backend shrank by **123.0 MiB** between medians. The two totals include all sampled processes at each instant, so summing individual medians need not reproduce them. Summed working set grew by **1,360.1 MiB** and can count shared pages more than once. The scratch CSV includes per-process sampled maxima and transient launch/evaluation processes. The observed marginal cost applies to this small fixture and common JVM/profile; larger solutions and independently restarted JVMs need their own measurements.

### Shutdown, real profile writes and restoration

At 04:54:19 I issued `remote-dev-server.exe stop C:\Dev\RoyderRemoteRound6\B\Probe.sln` in the same private environment. That stopped the shared host, including A. The Windows job reported **zero active PIDs**; none of the recorded PID/start-time identities survived a separate check, and no test port remained listening. There was **no forced kill**. The fact that the shared stop ended both solutions matters to per-worktree lifecycle design.

| Surface | Immediate before/after evidence | Action and verification |
|---|---|---|
| `%APPDATA%\JetBrains`, outside versioned IDE folders | All **48** files had identical SHA-256 content and the same names at preflight and at 04:54:27 after shutdown. Hash of the sorted relative-path/content-hash manifest: `88eb0632d67800372f4ebd96c9f7e564667b6b1c19abe588ca1c54f8a9fba03b`. `PermanentUserId` and `PermanentDeviceId` did acquire different last-write times during the experiment, on byte-identical files. They were touched again after the lab processes exited, so the writer of the timestamps alone is uncertain. | Restored just the two original timestamps after verifying the live bytes against the backups. Both timestamps matched the baseline again at 04:57:17. No ID contents were rewritten. |
| `%LOCALAPPDATA%\JetBrains` top-level entries and timestamps | One new entry, `RemoteDev`. Its `active` directory was empty. `recent` held exactly `217732`, `217732_-1236535152` and `217732_560416207`. Their JSON identified the test JVM; the per-project entries named the A and B lab paths. | After shutdown, moved this entirely lab-owned directory into private evidence. The final top-level listing, entry types, sizes and creation/last-write times matched all **29** pre-run entries. These remote status files may contain join links and should stay private. |
| Home Git status and active Rider settings | `git -C C:\Users\magnu status --short -- AppData/Roaming/JetBrains` was empty before, immediately after and after restoration. The expanded backup included **197** shared, Git-tracked and active Rider settings files. No monitored file had different contents at the immediate post-run snapshot. | No Git revert, commit or index operation was used to restore settings. |

**Concurrent activity left intact.** At 04:54:27, the files below still matched the pre-run snapshot. Afterwards, with every lab process gone, `acp-agents\installed.json` changed content, `PrivacyPolicy\aiEua.cached` changed timestamp and a discovery entry named `discovery\167088-ide-instance.json` briefly appeared and disappeared. PID 167088 was not a lab process. At the 04:57:17 audit the discovery entry had gone; the other two post-shutdown differences remained. I did not overwrite another session’s later work. The final home Git status and Local JetBrains top-level listing still matched their baselines.

**What this changes.** Clientless remote Rider is a demonstrated editing-capable Royder peer for the tested rename preview, and several solutions can share its JVM. The four IDE directory overrides and separate `JET_*` paths did **not** fully contain Remote Development status writes under the real Windows user account. Any production isolation claim must account for `Local\JetBrains\RemoteDev`. The requested snapshots verify the stated paths; they do not attribute every file operation under every versioned directory or the registry.

Private reproducibility evidence is under `%TEMP%\royder-headless\codex-1-round-6`. The safe summaries are `evidence/process-memory.csv`, the two sanitized status JSON files, per-solution health, the three exact tool results and `evidence/restoration.json`. The raw status files, join links and profile backups remain private. The disposable fixture folders were left on disk; no lab process was left running.
