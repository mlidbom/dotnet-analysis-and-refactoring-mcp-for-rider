package dev.magnus.royder.remotecloseworktree;

import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.application.ModalityState;
import com.intellij.openapi.diagnostic.Logger;
import com.intellij.openapi.progress.ProcessCanceledException;
import io.netty.channel.ChannelHandlerContext;
import io.netty.handler.codec.http.*;
import org.jetbrains.ide.RestService;

import java.lang.reflect.InvocationTargetException;
import java.net.InetSocketAddress;
import java.nio.file.InvalidPathException;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.util.concurrent.CancellationException;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.function.Supplier;

import static java.nio.charset.StandardCharsets.UTF_8;
import static io.netty.handler.codec.http.HttpResponseStatus.*;

/** The authenticated loopback management contract shared by the two built-in HTTP services. */
abstract class ManagedWorktreeService extends RestService {
   private static final Logger LOG = Logger.getInstance(ManagedWorktreeService.class);

   @Override protected final boolean isMethodSupported(HttpMethod method) { return HttpMethod.POST.equals(method); }

   @Override protected final boolean isHostTrusted(FullHttpRequest request, QueryStringDecoder url)
      throws InterruptedException, InvocationTargetException {
      var token = System.getenv("ROYDER_MANAGED_HOST_TOKEN");
      var authorization = request.headers().get(HttpHeaderNames.AUTHORIZATION);
      return token != null && !token.isBlank() && authorization != null
          && MessageDigest.isEqual(("Bearer " + token).getBytes(UTF_8), authorization.getBytes(UTF_8))
          && super.isHostTrusted(request, url);
   }

   String instanceHeader(FullHttpRequest request) { return request.headers().get(HttpHeaderNames.IF_MATCH); }

   abstract CompletableFuture<WorktreeReply> handle(String route, Path solution, String ifMatch);

   @Override public final String execute(QueryStringDecoder url, FullHttpRequest request, ChannelHandlerContext context) {
      var channel = context.channel();
      var version = request.protocolVersion();
      var keepAlive = HttpUtil.isKeepAlive(request);
      if (!(channel.remoteAddress() instanceof InetSocketAddress peer) || !peer.getAddress().isLoopbackAddress()) {
         sendStatus(FORBIDDEN, keepAlive, channel);
         return null;
      }

      final Path solution;
      try {
         solution = Path.of(request.content().toString(UTF_8)).normalize();
      } catch (InvalidPathException invalid) {
         return "Invalid solution path";
      }
      if (!solution.isAbsolute()) return "An absolute solution filename is required";
      handle(url.path(), solution, instanceHeader(request))
         .whenComplete((answer, error) -> (error == null ? answer : failure(error)).send(channel, version, keepAlive));
      return null;
   }

   static <T> CompletableFuture<T> onDispatchThread(Supplier<T> operation) {
      return CompletableFuture.supplyAsync(operation, task ->
         ApplicationManager.getApplication().invokeLater(task, ModalityState.nonModal()));
   }

   static CompletableFuture<WorktreeReply> reply(HttpResponseStatus status, String message) {
      return CompletableFuture.completedFuture(new WorktreeReply(status, message));
   }

   private static WorktreeReply failure(Throwable error) {
      while (error instanceof CompletionException && error.getCause() != null) error = error.getCause();
      if (error instanceof ProcessCanceledException || error instanceof CancellationException)
         return new WorktreeReply(CONFLICT, "Rider cancelled the operation");
      LOG.warn("Worktree management request failed", error);
      return new WorktreeReply(INTERNAL_SERVER_ERROR, "Rider could not complete the worktree request");
   }
}
