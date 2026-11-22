<!-- By the Codex helper royder-codex-1, 2026-11-22, its round 3: measured with a scratch copy of experiments/HeadlessHost at bc4dcf6 and scripts that stayed in its Temp folder, as did the evidence under runs/. Kept as it was written, except that the patcher's source is linked in RoslynPatcher/, where its byte-identical copy is. The patcher, and the source generator and analyzer the fixture used, are beside this file. What later runs disprove is noted in dev_docs/backlog.md. -->

# Headless Royder round 3 — outbound HTTPS and worktree removal

**The outbound HTTPS request is JetBrains’ .NET product update check. The Roslyn working-directory patcher passed the decision test.** The engine indexed and answered `find_usages` after the update request failed. With the patcher installed, plain `git worktree remove` succeeded on its first attempt **during indexing and while idle**, with the same Roslyn and MSBuild processes alive before and after each removal. The real solution kept its team settings, generated symbols and analyzer diagnostics.

The [full patcher source](RoslynPatcher/HeadlessSolutionProcessStartInfoPatcher.cs) is beside this report. The [buildable component project](RoslynPatcher/Royder.Headless.RoslynPatcher.csproj) contains the same source and its [zone marker](RoslynPatcher/ZoneMarker.cs).

## 1. What the engine requests

| Question | Observed answer |
|---|---|
| Host and resource | `https://www.jetbrains.com/dotnet/updates/dotnet.Updates.xslt`. The direct run resolved `www.jetbrains.com`, connected to **65.9.46.26:443**, used `www.jetbrains.com` as its TLS target, and received HTTP 200. A separate DNS lookup returned `65.9.46.26`, `.76`, `.83` and `.99` for that hostname. This connects the previous IP observations to the same CDN pool; the request trace itself is from this round. |
| Caller | `System.Net.Http`’s synchronous `RequestStart` stack included `JetBrains.Application.Updates.UpdatesManager.CheckForUpdates`’s background delegate, `CheckForUpdatesImpl` and `GetUpdatesContent`. The installed `JetBrains.Platform.Shell.dll` names a separate thread for the check, fetches the content with `SecureWebClient.OpenRead`, and parses update details including target product, qualifying versions and new version. |
| Data in the URL | The diagnostic recorder kept query **keys** and omitted values. Observed keys included `api`, `mid`, `SessionId`, product mode/version/build/suffix/EAP/development flags, product name and check-for-update flag, `License.0.Type`, `License.0.Entities.0`, OS/runtime versions, environment flags and `Hash`. License-related fields occur in this **update** request. The observed call stack and returned-content handler establish its purpose. No feature-flag, usage-statistics or licensing component appeared in that stack. |
| Timeout and switch | `GetUpdatesContent` constructs `SecureWebClient(TimeSpan.FromSeconds(10))`. `CheckForUpdates` recognizes `JETBRAINS_NO_CHECK_FOR_UPDATES`. Subsequent engine runs with that variable set in the child environment emitted zero `System.Net.Http.RequestStart` events. No machine-wide proxy, firewall, hosts-file or certificate-store change was made. |

I added an `EventListener` to the scratch executable before ReSharper startup. It recorded `System.Net.Http`, `System.Net.NameResolution`, `System.Net.Security` and `System.Net.Sockets`, plus the managed call stack at request start. The filter preserved the endpoint and omitted machine/session/license query values. I also recorded PID-owned connections externally. No TLS interception was used.

### Does startup wait?

**Verified opt-out.** Set `JETBRAINS_NO_CHECK_FOR_UPDATES=1` in the engine’s environment before launch to suppress this request. In `runs/finalidle`, engine PID 49948 indexed all three projects, ran diagnostics and returned the generated member’s usage. From startup through exit, the same four .NET event sources recorded **0 HTTP request starts, 0 DNS lookup starts, 0 TLS handshake starts and 0 socket connect starts** under the diagnostic filter. The independent process-tree socket sampler observed only the two loopback worker connections. The stock engine in `runs/stock` gave the same zero counts with the switch set. Without the switch, `runs/netdirect` recorded one of each and HTTP 200. The evidence is in each run’s `events.jsonl`; `decision.py` sets the child environment.

| Per-process test | HTTP outcome, measured inside the engine | Three-project fixture outcome |
|---|---|---|
| Normal network | HTTP 200 after **0.21 s** | Indexed in **12.652 s**; `IShape.Area` returned three lines in 317 ms. |
| Closed proxy | The engine attempted **127.0.0.1:1**. Windows returned **10061**. `RequestFailed` followed **2.09 s** after `RequestStart`. No direct retry appeared. | Indexed in **10.740 s**; same three lines in 332 ms; exit 0. |
| Proxy accepts CONNECT and withholds its response until `READY` | The private proxy independently recorded `CONNECT www.jetbrains.com:443 HTTP/1.1`. The engine timed out after **10.00 s**. The proxy waited **10.77 s** from CONNECT to `READY` before sending 503, after the client had already timed out. | MSBuild and Roslyn started while the request was still outstanding. The engine indexed in **12.163 s**, returned the same three lines in 501 ms, and exited 0. |

The three locations were `Shapes.Drawing/Drawing.cs:12`, `Shapes.Drawing/Drawing.cs:14` and `Shapes/ShapeExtensions.cs:6`. A further held-proxy run on a one-project solution also timed out at about ten seconds and returned the one in-scope usage. **The ten seconds belong to the HTTP worker.** Startup and project loading progressed concurrently, and no successful HTTPS response was needed. Neither held run delivered its first query result before the client-side timeout. These timings do not establish a fixed startup penalty. Early progress messages used receipt timestamps from redirected stderr; the HTTP figures above use timestamps recorded inside the engine.

Scratch evidence: `runs/netdirect/events.jsonl`, `runs/netfailed/events.jsonl`, `runs/netheld/{events.jsonl,proxy.json}` and `runs/netheldsmall/{events.jsonl,proxy.json}`. `HeadlessHost/NetworkTrace.cs` and `held_update.py` implement the two independent observations.

## 2. Moving the Roslyn working directory

### What actually composed

I built `HeadlessSolutionProcessStartInfoPatcher` as a subtype of the stock CLI `CommandLineSolutionProcessStartInfoPatcher` with `[SolutionInstanceComponent(Instantiation.DemandAnyThreadSafe)]`. It matches the full path of the bundled `JetBrains.Roslyn.Worker.exe` **before** the stock patcher turns the launch into `dotnet exec`. For that launch it clones `JetProcessStartInfo`, sets the clone’s working directory to `<private TEMP>/RoslynWorker` and delegates to the base implementation. All other inputs go directly to the base implementation. The launcher’s contract is one private temporary directory outside the worktree.

The scratch host retained `CommandLineProductMain.Main(..., isLiveSession: true)` and `IInspectCodeEnvironmentZone`. The only integration needed to discover the component was adding `CommandLinePluginsMixin<InspectCodeSettingsData>` via `host.CustomAdd(...)` and pointing `-x=Royder.Headless.RoslynPatcher` and `--source` at the private local feed. The package placed its DLL in `DotFiles/` and declared `Wave [263.0.0]`. No manual registration of the patcher, change to Royder’s host ABI, or private solution file was needed.

A live `solution.GetComponent<IProcessStartInfoPatcher>()` returned `Royder.Headless.RoslynPatcher.HeadlessSolutionProcessStartInfoPatcher` in the patched host. The stock control returned `JetBrains.ReSharperAutomationTools.CommandLine.Common.Console.BatchTool.MsBuild.CommandLineSolutionProcessStartInfoPatcher`. Windows reported the child’s actual working directory independently. The final build and both deployed copies had SHA-256 `e3a4f43ecf8b6e551e221006c6709ac7f09da37c506319f2f516e812c834ec68`.

### First-attempt plain Git removal

| Final run | Proof of timing | Result |
|---|---|---|
| **During indexing** (`finalindex`) | After design-time MSBuild reported completion, the Roslyn worker reported setup complete, and a symbol-cache file existed. Git ran at **02:05:12.255–12.856 UTC**. `INDEXED` was observed at **02:05:15.543 UTC**. | **Exit 0**, root and registration gone. Roslyn PID **20408** had `runs/finalindex/temp/RoslynWorker` as its working directory before and after. MSBuild PID **99896** was also alive across deletion. |
| **Idle after a semantic query** (`finalidle`) | The host emitted the diagnostics, usage and `READY`. Git then ran at **02:05:34.807–35.085 UTC**. | **Exit 0**, root and registration gone. Roslyn PID **25844** remained in `runs/finalidle/temp/RoslynWorker`; MSBuild PID **7760** remained alive. |

A separate prestarted PowerShell invoked `git -C <private-repository> worktree remove <absolute-path>` once without force. It ran outside the worktree. Both host processes were still alive when ownership, registration and path disappearance were checked. Neither directory reappeared at the follow-up check. The earlier `fixindex` and `fixidle` runs gave the same results on the implementation before the final naming/documentation pass. This reverses the previous stock-host result: Git had returned 255 and left an empty root while Roslyn used it as its current directory.

### Semantic decision test

The private fixture retained its real solution file. I added a small [source generator](FixtureChecks/FixtureGenerator.cs) and [diagnostic analyzer](FixtureChecks/FixtureAnalyzer.cs) as an analyzer reference in `Shapes`. The assembly itself was inside the worktree during the removal tests. No generator-produced .cs file was checked in.

| Check | Patched host, team settings present | Stock host, same fixture, team settings present |
|---|---|---|
| MSBuild project model | `Shapes`, `Shapes.Drawing`, `Shapes.Web`, each `net10.0` | Identical names and frameworks |
| Generated member | `IFinder.FindReferences` on generated `RoundThreeGenerated.Twice` returned exactly `Shapes/RoundThreeUsage.cs:7` | Same file and line |
| Roslyn analyzer | The real per-file ReSharper daemon returned `RoslynHighlighting`, severity **WARNING**, message “Round three analyzer reached RoundThreeAnalyzerTarget” | Same highlighting, severity and message |
| Team setting | `RedundantUsingDirective` returned with effective severity **ERROR** | **ERROR**; a third control with only the real solution’s `.slnx.DotSettings` moved aside returned **WARNING** |

The analyzer’s [descriptor](FixtureChecks/FixtureAnalyzer.cs) is `ROYDER301`. The per-file daemon returned its unique message as a Roslyn highlighting; this API supplied no configurable inspection ID for that highlighting. The collector reported seven highlightings and no incomplete-stage message in both comparison runs. The executable used the same `UsageSearch` as the original spike and a scratch copy of Royder’s `DaemonHighlightingCollector`. This tested the engine, component selection and daemon output without binding an MCP endpoint.

Scratch evidence: `runs/finalindex/result.json`, `runs/finalidle/{result.json,events.jsonl}`, `finalindex-git.jsonl`, `finalidle-git.jsonl`, `runs/stock/events.jsonl` and `runs/defaultseverity/events.jsonl`.

## Recommendation and limits

**Use the process patcher for the next headless-host increment.** It preserved the real solution, its settings and the tested Roslyn services and removed the demonstrated Windows handle obstruction. A private solution file would add another solution identity without a benefit shown by this test.

Removal succeeding and the process stopping are separate requirements. In the indexing run the stock live engine even emitted `INDEXED` after Git had already removed the tree. The lifecycle owner still needs to invalidate that worktree identity and stop its worker tree. The launcher explicitly ended each test engine. The experiment establishes behavior on Windows with `jb` 2026.3.0.1, one generator, one analyzer and these three projects. It does not establish that every third-party analyzer resolves its own relative files independently of process cwd, or address design-time `obj` writes.

**Handoff.** The standalone source beside this report and the copy in `RoslynPatcher/` are byte-identical. The latter is ready to package with its project and `ZoneMarker.cs`. The scratch host is based on main `bc4dcf6f3a565008df32369d3b40dc0e772cc159`; its loader/observability changes are in `HeadlessHost/Program.cs` and `SemanticChecks.cs`. `make_fixture.py` builds the private package and semantic fixture. `decision.py` drives each of the `stock`, `noteam`, `indexing` and `idle` cases. The package build finished with zero warnings and errors. The instrumented host build surfaced three nullable annotations in the copied collector.

Only one engine ran at a time. The launcher checked available RAM before launch and monitored it and owned sockets while the engine ran. All eleven runs stayed above the 2 GiB stop threshold; the lowest observed available memory was **8.96 GiB**. No reserved or non-loopback listener was observed. All recorded engine/child process identities had exited at the final check. All work and fixtures were confined to scratch; no product deployment or main-checkout edit was made.
