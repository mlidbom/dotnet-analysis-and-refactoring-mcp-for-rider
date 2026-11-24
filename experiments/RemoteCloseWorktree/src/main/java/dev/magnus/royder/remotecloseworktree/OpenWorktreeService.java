package dev.magnus.royder.remotecloseworktree;

import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.application.ModalityState;
import com.intellij.openapi.diagnostic.Logger;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.project.ProjectManager;
import com.intellij.openapi.vfs.LocalFileSystem;
import com.jetbrains.rider.projectView.SolutionManager;
import com.jetbrains.rider.projectView.solutionFileTypes.SolutionFileTypesHelper;
import com.jetbrains.rider.services.security.TrustedSolutionManager;
import com.jetbrains.rider.services.security.TrustedSolutionStore;
import kotlin.coroutines.EmptyCoroutineContext;
import kotlinx.coroutines.BuildersKt;

import io.netty.handler.codec.http.FullHttpRequest;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.LinkOption;
import java.nio.file.Path;
import java.util.Locale;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionException;
import java.util.concurrent.ConcurrentHashMap;

import static io.netty.handler.codec.http.HttpResponseStatus.*;

/** Opens or adopts one exact solution. Requests racing to open it share the same Rider operation. */
public final class OpenWorktreeService extends ManagedWorktreeService {
   private static final Logger LOG = Logger.getInstance(OpenWorktreeService.class);
   private final ConcurrentHashMap<Path, CompletableFuture<WorktreeReply>> opening = new ConcurrentHashMap<>();

   @Override protected String getServiceName() { return "royder-open-worktree"; }

   @Override String instanceHeader(FullHttpRequest request) { return request.headers().get("X-Royder-Open-Id"); }

   @Override CompletableFuture<WorktreeReply> handle(String route, Path solution, String openId) {
      if (openId != null && !openId.matches("[0-9a-f]{32}")) return reply(BAD_REQUEST, "Invalid open request ID");
      if (route.equals("/api/" + getServiceName() + "/status")) {
         return onDispatchThread(() -> {
            if (opening.containsKey(solution)) return new WorktreeReply(CONFLICT, "Open still in progress");
            var matches = OpenProjectInstance.at(solution, ProjectManager.getInstance().getOpenProjects());
            if (matches.isEmpty()) return new WorktreeReply(NOT_FOUND, "No open project for the solution");
            if (matches.size() != 1) return new WorktreeReply(CONFLICT, "Ambiguous open project");
            var registered = OpenProjectInstance.of(matches.get(0));
            if (openId != null && (registered == null || !openId.equals(registered.openId())))
               return new WorktreeReply(PRECONDITION_FAILED, "This project was not registered by the requested open");
            return new WorktreeReply(OK, "", registered == null ? null : registered.etag());
         });
      }
      if (route.equals("/api/" + getServiceName() + "/private")) {
         if (PrivateProjectStore.configured() == null) return reply(CONFLICT, "Private project store was not configured at startup");
      } else if (!route.equals("/api/" + getServiceName())) return reply(NOT_FOUND, "Unknown operation");
      var name = solution.getFileName();
      if (name == null) return reply(BAD_REQUEST, "Expected a solution filename");
      var suffix = name.toString().toLowerCase(Locale.ROOT);
      if (!suffix.endsWith(".sln") && !suffix.endsWith(".slnx")) return reply(BAD_REQUEST, "Expected a .sln or .slnx");
      var result = opening.computeIfAbsent(solution, path -> CompletableFuture.supplyAsync(() -> open(path, openId),
         task -> ApplicationManager.getApplication().executeOnPooledThread(task)));
      result.whenComplete((answer, error) -> opening.remove(solution, result));
      return result;
   }

   /** Runs on the pool. Only project matching, registration and the update to Rider's trust store require EDT work. */
   private static WorktreeReply open(Path solution, String openId) {
      if (!Files.isRegularFile(solution, LinkOption.NOFOLLOW_LINKS)) return new WorktreeReply(NOT_FOUND, "Solution file not found");
      try {
         if (!solution.toRealPath().equals(solution)) return new WorktreeReply(BAD_REQUEST, "Use the physical solution path");
         var disk = WorktreeDiskIdentity.read(solution);
         var existing = onDispatchThread(() -> {
            var matches = OpenProjectInstance.at(solution, ProjectManager.getInstance().getOpenProjects());
            if (matches.size() > 1) return new WorktreeReply(CONFLICT, "Multiple open projects match the solution");
            if (matches.isEmpty()) return null;
            var privateStore = PrivateProjectStore.configured();
            if (privateStore != null && !privateStore.contains(matches.get(0)))
               return new WorktreeReply(CONFLICT, "The existing project uses settings outside this private server");
            return OpenProjectInstance.register(matches.get(0), solution, disk, openId);
         }).join();
         return existing != null ? existing : openInRider(solution, disk, openId);
      } catch (IOException error) {
         LOG.warn("Cannot establish the filesystem identity of " + solution, error);
         return new WorktreeReply(CONFLICT, "Cannot establish the worktree's filesystem identity: " + error.getMessage());
      }
   }

   private static WorktreeReply openInRider(Path solution, WorktreeDiskIdentity disk, String openId) throws IOException {
      var file = LocalFileSystem.getInstance().refreshAndFindFileByNioFile(solution);
      if (file == null || !SolutionManager.INSTANCE.isSolutionFile(file)) return new WorktreeReply(NOT_FOUND, "Rider cannot open this solution file");
      if (!TrustedSolutionManager.INSTANCE.findUntrustedAncestorConfigFiles(solution.getParent()).isEmpty())
         return new WorktreeReply(CONFLICT, "Rider requires a separate trust decision for configuration outside the solution");

      // SolutionManager uses this per-solution store location when opening an existing .sln or .slnx.
      var types = SolutionFileTypesHelper.Companion.getInstance();
      var displayName = types.getSolutionDisplayName(file);
      var ideaName = types.getSolutionIdeaName(solution, displayName);
      var ideaDirectory = solution.getParent().resolve(".idea").resolve(".idea." + ideaName);
      if (!disk.stillAt(solution)) return new WorktreeReply(CONFLICT, "Worktree changed before open");
      ApplicationManager.getApplication().invokeAndWait(
         () -> TrustedSolutionStore.Companion.getInstance().assumeTrusted(ideaDirectory), ModalityState.nonModal());
      try {
         // No project to replace, a new frame, normal restore, ordinary Rider trust checks.
         var privateStore = PrivateProjectStore.configured();
         var project = privateStore == null
            ? BuildersKt.<Project>runBlocking(EmptyCoroutineContext.INSTANCE, (scope, continuation) ->
                 SolutionManager.INSTANCE.openExistingSolution(null, true, file, true, false, continuation))
            : privateStore.open(solution, file, displayName, ideaDirectory, disk);
         var opened = onDispatchThread(() -> OpenProjectInstance.register(project, solution, disk, openId)).join();
         if (!opened.status().equals(OK)) return opened;
         try {
            if (disk.stillAt(solution)) return opened;
         } catch (IOException inaccessible) {
            return new WorktreeReply(CONFLICT, "Cannot verify the worktree after open; retain its tag for discard", opened.etag());
         }
         return new WorktreeReply(CONFLICT, "Worktree changed during open; discard the original instance", opened.etag());
      } catch (InterruptedException interrupted) {
         Thread.currentThread().interrupt();
         throw new CompletionException(interrupted);
      }
   }
}
