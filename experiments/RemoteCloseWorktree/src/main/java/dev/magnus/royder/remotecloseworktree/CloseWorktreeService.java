package dev.magnus.royder.remotecloseworktree;

import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.application.ModalityState;
import com.intellij.openapi.diagnostic.Logger;
import com.intellij.openapi.progress.ProcessCanceledException;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.project.ProjectManager;
import com.jetbrains.rider.projectView.SolutionHostExtensionsKt;
import io.netty.channel.ChannelHandlerContext;
import io.netty.handler.codec.http.FullHttpRequest;
import io.netty.handler.codec.http.HttpHeaderNames;
import io.netty.handler.codec.http.HttpMethod;
import io.netty.handler.codec.http.HttpResponseStatus;
import io.netty.handler.codec.http.HttpUtil;
import io.netty.handler.codec.http.QueryStringDecoder;
import org.jetbrains.ide.RestService;

import java.lang.reflect.InvocationTargetException;
import java.net.InetSocketAddress;
import java.nio.file.InvalidPathException;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.util.Arrays;

import static java.nio.charset.StandardCharsets.UTF_8;
import static io.netty.handler.codec.http.HttpResponseStatus.*;

/** Handles a manager's request to close one file-backed Rider project in this JVM. */
public final class CloseWorktreeService extends RestService {
   private static final String SERVICE = "royder-close-worktree";
   private static final Logger LOG = Logger.getInstance(CloseWorktreeService.class);

   @Override protected String getServiceName() { return SERVICE; }
   @Override protected boolean isMethodSupported(HttpMethod method) { return HttpMethod.POST.equals(method); }

   @Override protected boolean isHostTrusted(FullHttpRequest request, QueryStringDecoder url)
      throws InterruptedException, InvocationTargetException {
      var token = System.getenv("ROYDER_MANAGED_HOST_TOKEN");
      var authorization = request.headers().get(HttpHeaderNames.AUTHORIZATION);
      return token != null && !token.isBlank() && authorization != null
          && MessageDigest.isEqual(("Bearer " + token).getBytes(UTF_8), authorization.getBytes(UTF_8))
          && super.isHostTrusted(request, url);
   }

   @Override public String execute(QueryStringDecoder url, FullHttpRequest request, ChannelHandlerContext context) {
      var channel = context.channel();
      var keepAlive = HttpUtil.isKeepAlive(request);
      if (!(channel.remoteAddress() instanceof InetSocketAddress peer) || !peer.getAddress().isLoopbackAddress()) {
         sendStatus(FORBIDDEN, keepAlive, channel);
         return null;
      }
      if (!url.path().equals("/api/" + SERVICE)) return "Unknown operation";

      final Path solution;
      try {
         solution = Path.of(request.content().toString(UTF_8)).normalize();
      } catch (InvalidPathException invalid) {
         return "Invalid solution path";
      }
      if (!solution.isAbsolute()) return "An absolute solution filename is required";

      // A worktree may be closed and reopened while the EDT is busy. Only projects open at receipt may be closed.
      var openWhenRequested = ProjectManager.getInstance().getOpenProjects();
      ApplicationManager.getApplication().invokeLater(() -> {
         HttpResponseStatus status;
         try {
            status = closeOpenSolution(solution, openWhenRequested);
         } catch (ProcessCanceledException canceled) {
            status = CONFLICT;
         } catch (RuntimeException failure) {
            LOG.warn("Could not close the requested worktree", failure);
            status = INTERNAL_SERVER_ERROR;
         }
         sendStatus(status, keepAlive, channel);
      }, ModalityState.nonModal());
      return null;
   }

   private static HttpResponseStatus closeOpenSolution(Path solution, Project[] openWhenRequested) {
      var matches = Arrays.stream(openWhenRequested)
         .filter(project -> !project.isDisposed() && project.isOpen() && SolutionHostExtensionsKt.getHasSolution(project))
         .filter(SolutionHostExtensionsKt::isExistingSolution)
         .filter(project -> Path.of(SolutionHostExtensionsKt.getSolutionPath(project)).normalize().equals(solution))
         .toList();
      if (matches.isEmpty()) return NOT_FOUND;
      if (matches.size() > 1) return CONFLICT;
      return ProjectManager.getInstance().closeAndDispose(matches.get(0)) ? OK : CONFLICT;
   }
}
