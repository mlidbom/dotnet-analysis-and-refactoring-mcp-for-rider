# Closing one worktree in a shared Rider remote-dev server

**Close the IntelliJ `Project` in the host JVM.** In Rider 2026.3.0.1 (`RD-263.7307.325`) the JVM owns the
open projects and each project's connection to its full ReSharper backend. I found no project-scoped Rider
lifecycle request that a .NET backend can send to make the JVM close its own project. A small JVM extension in
Royder's existing JAR can accept a request from Royder's primary and call `ProjectManager.closeAndDispose(project)`.
Opening another worktree already has a stock command.

I read the installed class files under `%LOCALAPPDATA%\Programs\Rider`, the matching .NET assemblies under
`C:\Program Files\JetBrains\Rider\r2r`, and Royder's Gradle configuration. The close path below is verified
in code. The proposed handler was neither built nor run. Opening two solutions in one clientless JVM was observed
in the earlier experiment. [Round 6 run](rider-remote-dev-server.md#round-6--observed-on-magnuss-machine-22-november-2026)

## 1. Which side initiates a close

The generated .NET types are in `JetBrains.Platform.RdProtocol.dll`. Their JVM counterparts are in
`lib/intellij.rd.ide.model.generated.jar` and `lib/intellij.rider.model.generated.jar`. The direction below
comes from the actual writer and subscriber as well as the generated signatures.

| Installed member | Writer, reader and effect |
|---|---|
| `Solution.CloseRequested` | JVM `com.jetbrains.rider.protocol.components.SolutionHost.remove` sets it to `RdSolutionCloseOptions(false)` and clears the documents and solution map on that project's protocol. In `Rider.Backend.dll`, `RiderSolutionWatcher` subscribes, checks the current solution instance and calls `RiderSolutionManager.CloseSolution()`. The .NET property's type, `IAsyncProperty<RdSolutionCloseOptions>`, even exposes a setter. The receiver I found closes the ReSharper solution. There is no corresponding receiver here that closes an IntelliJ project. |
| `RdShellModel.AfterCloseSolution` | JVM `RiderSolutionEventsTracker.afterProjectClosed(project)` fires it on that project's bound protocol. The .NET `RiderSolutionEventsTracker` subscribes and raises a local notification. The IntelliJ project has already closed at this point. |
| `RdShellModel.ShutdownRequest` | JVM `ShellHost.requestShutdown` sets it. The .NET side exposes `IViewableProperty<bool>`. `ShutdownWhenModelRequests` observes it and requests shutdown of the .NET application. |
| `SolutionModel`, `SolutionLifecycle`, `RiderSolutionLifecycle` | The .NET solution map is an `IViewableMap<int, Solution>`. The lifecycle models carry startup, readiness and synchronization. I found no close-own-project command in them. `ActionsModel.ExecuteAction` is a different facility. The JVM subscribes in a `RiderActionExecuteSession` with an IDE action context. It supplies no project-close contract for an unattended HTTP call. |
| `CodeWithMeConnectionExtModel.closeProject` | This belongs to the **JetBrains Client connection**. The JVM's `CloseProjectListener` checks `isController`, resolves the project ID, and runs `saveAndForceCloseProject` under write intent. A .NET solution backend has a different protocol and session. [Remote host lifecycle](remote-dev-server-lifecycle.md#2-closing-one-project) |

**What happens to the PID.** In `lib/intellij.rider.rdclient.dotnet.jar`, `SolutionInitializerService` registers
a termination callback on the IntelliJ **project's** lifetime. It calls `SolutionHost.remove(solution)` and
`releaseProtocolHost` for the captured backend. `ProtocolManager.release` releases that host and, when more
than one protocol host is present, removes it and calls `requestShutdown` on that one. This reaches
`ReSharperProcessBase.requestShutdown` and the project backend's `ShellHost.requestShutdown`. With the default
release arguments the last protocol host can be kept. A second project's protocol is outside the callback.
For the requested two-project case I therefore expect the target backend PID to exit and the sibling to keep serving.
The return from closing the project establishes neither PID exit nor release of a Roslyn worker's directory handle.
Those are separate observations to make before removing the worktree.

## 2. The JVM extension and the HTTP request

**Proposed.** Give the managed host one POST operation in IntelliJ's existing built-in HTTP server. Royder's
primary sends it the **registered absolute solution filename** for the worktree. Two `ReSharperMcp.sln` files
can share a display name. IntelliJ's `Project.basePath` identifies a directory. For a file-backed Rider project,
`SolutionHostExtensionsKt.getSolutionPath(project)` resolves Rider's solution directory plus its solution filename.
Match exactly one open project and keep that object for the close.

Keep the existing `com.intellij.modules.rider` dependency. The installed `intellij.rider.xml` names both
`intellij.rider.rdclient.dotnet` and `intellij.platform.builtInServer.impl`. Add one entry under Royder's existing
`<extensions defaultExtensionNs="com.intellij">`. `intellij.platform.builtInServer.impl.xml` declares this
extension point and uses it for the bundled `RestService` handlers. The remote host's own status handler uses it too.
[Royder descriptor](../../rider-plugin/src/main/resources/META-INF/plugin.xml)

```xml
<httpRequestHandler implementation="dev.magnus.dotnetanalysisandrefactoringmcp.CloseWorktreeService"/>
```

The handler below sketches the request path. It accepts UTF-8 plain text at
`POST /api/royder-close-worktree`. The supervisor provisions a fresh bearer token for the private host and its
management caller. Leave the token unset on everyday Riders. The primary must authorize the caller for the
target worktree before forwarding the request with the host credential. Use the host's actual built-in HTTP port
(`BuiltInServerManager.getInstance().waitForStart().getPort()` in the JVM). The supervisor can record the port
belonging to that JVM at startup. For a one-class spike, configure `-Drpc.port` and have the supervisor verify
that the managed JVM owns the chosen port before sending a credential. The built-in server can fall back to
another port. The Remote Development listener and Royder's MCP port serve different protocols.

```java
package dev.magnus.dotnetanalysisandrefactoringmcp;

import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.application.WriteIntentReadAction;
import com.intellij.openapi.project.ProjectManager;
import com.jetbrains.rider.projectView.SolutionHostExtensionsKt;
import io.netty.channel.ChannelHandlerContext;
import io.netty.handler.codec.http.*;
import java.net.InetSocketAddress;
import java.nio.file.InvalidPathException;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.util.Arrays;
import org.jetbrains.ide.RestService;
import static java.nio.charset.StandardCharsets.UTF_8;
import static io.netty.handler.codec.http.HttpResponseStatus.*;

public final class CloseWorktreeService extends RestService {
   @Override protected String getServiceName() { return "royder-close-worktree"; }
   @Override protected boolean isMethodSupported(HttpMethod method) { return HttpMethod.POST.equals(method); }

   @Override public String execute(QueryStringDecoder url, FullHttpRequest request, ChannelHandlerContext context) {
      var token = System.getenv("ROYDER_MANAGED_HOST_TOKEN");
      var supplied = request.headers().get(HttpHeaderNames.AUTHORIZATION);
      var channel = context.channel();
      boolean keepAlive = HttpUtil.isKeepAlive(request);
      if (!(channel.remoteAddress() instanceof InetSocketAddress peer) || !peer.getAddress().isLoopbackAddress()
          || token == null || token.isBlank() || supplied == null
          || !MessageDigest.isEqual(("Bearer " + token).getBytes(UTF_8), supplied.getBytes(UTF_8))) {
         sendStatus(FORBIDDEN, keepAlive, channel);
         return null;
      }
      if (!url.path().equals("/api/royder-close-worktree")) return "Unknown operation";
      final Path wanted;
      try {
         wanted = Path.of(request.content().toString(UTF_8)).normalize();
      } catch (InvalidPathException invalid) {
         return "Invalid solution path";
      }
      if (!wanted.isAbsolute()) return "An absolute solution filename is required";

      ApplicationManager.getApplication().invokeLater(() -> WriteIntentReadAction.run(() -> {
         try {
            var manager = ProjectManager.getInstance();
            var matches = Arrays.stream(manager.getOpenProjects())
               .filter(project -> !project.isDisposed() && SolutionHostExtensionsKt.getHasSolution(project))
               .filter(SolutionHostExtensionsKt::isExistingSolution)
               .filter(project -> Path.of(SolutionHostExtensionsKt.getSolutionPath(project)).normalize().equals(wanted))
               .toList();
            if (matches.size() != 1) {
               sendStatus(matches.isEmpty() ? NOT_FOUND : CONFLICT, keepAlive, channel);
               return;
            }
            sendStatus(manager.closeAndDispose(matches.get(0)) ? OK : CONFLICT, keepAlive, channel);
         } catch (RuntimeException failure) {
            com.intellij.openapi.diagnostic.Logger.getInstance(CloseWorktreeService.class).warn(failure);
            sendStatus(INTERNAL_SERVER_ERROR, keepAlive, channel);
         }
      }));
      return null;
   }
}
```

**Why this close API.** In this build the public `ProjectManager.closeAndDispose` calls the implementation with
`saveProject=true`, `dispose=true`, `checkCanClose=true`. A veto can be reported to the caller.
The remote controller's `ProjectManagerEx.saveAndForceCloseProject` saves and disposes with
`checkCanClose=false` and carries `@ApiStatus.Internal`. The implementation asserts write-intent access and
rejects an enclosing write action. Schedule the work on the event dispatch thread and use write intent as the
installed listener does. The example copies request data before Netty releases the request. `RestService`
handles method, route and platform trust checks. Its `execute` contract lets the handler send the response
asynchronously and return null.

This class would go under `rider-plugin/src/main/java` and be packaged by the existing Kotlin/JVM and IntelliJ
Platform Gradle build with its Java 21 toolchain. No extra server library or RD model is needed. The proposed
handler still needs compilation and Plugin Verifier against the target Rider, particularly for the Rider path
accessor and `WriteIntentReadAction`, which is marked experimental. [Gradle build](../../rider-plugin/build.gradle.kts)

**Closing the primary itself.** The JVM remains able to send the HTTP response after closing a project. If the
outgoing HTTP client lives in the target .NET process, that client can disappear first. Have the primary acknowledge
the management operation before dispatching it and let the supervisor confirm completion independently. Drain
in-flight edits first. For retries, remember which open instance was requested so a later reopen at the same path
cannot be closed by an old request. A receipt alone is insufficient grounds to remove the worktree.

## 3. Opening a second worktree

**Verified in the installed code.** Run `remote-dev-server.exe run "<absolute path to the second solution>"`
with the **same IDE config and system directories** as the managed JVM, preserving the first launch's environment
and listener options. `StartupUtil.lockSystemDirs` gives those directories to `DirectoryLock.lockOrActivate`.
The config directory's `.lock` and the system directory's `.port` identify the running instance for command
activation. Selecting it by a Remote Development port alone would be insufficient.

The registered `remoteDevHost` starter, `UnattendedHostStarter`, returns true from
`canProcessExternalCommandLine`. Its external-command handler parses the project argument, makes it absolute
and calls `UnattendedModeManagerImpl.openProject`. In `UnattendedModeManagerImpl.Companion.prepareProject`:

1. An open project with the same stored `UNATTENDED_HOST_PROJECT_PATH_KEY` string is returned. Use the same
   absolute spelling on repeat requests.
2. For a new path it configures an `OpenProjectTaskBuilder`, explicitly sets `forceOpenInNewFrame=true`,
   and calls `ProjectUtil.openOrImportAsync`. The prior project stays open. The new project's original path is
   stored for subsequent requests. Opening can be cancelled. The launcher returning does not mean indexing is done.

These classes are in `plugins/remdev-plugin/lib/modules/intellij.platform.remoteController.backend.jar`.
Round 6 used the same host profile and listener settings with the full paths to A and B. It observed one JVM,
two full backend PIDs and no connected controller. For Royder, readiness was the requested solution path present
in health with indexing complete. [Run and measurements](rider-remote-dev-server.md#round-6--observed-on-magnuss-machine-22-november-2026)

**Open verification.** In a future two-worktree run, close B by its exact registered path, observe B leave host
status and its original backend PID exit, and query A through Royder with A's original JVM and backend PIDs.
Check first-try worktree removal separately. Repeat with the primary as target to verify acknowledgement and
peer takeover. This study performed none of those live operations.
