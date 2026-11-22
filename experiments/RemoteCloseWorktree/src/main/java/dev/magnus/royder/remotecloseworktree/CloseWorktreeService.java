package dev.magnus.royder.remotecloseworktree;

import com.intellij.openapi.project.ProjectManager;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.concurrent.CompletableFuture;

import static io.netty.handler.codec.http.HttpResponseStatus.*;

/** Normal close saves. Administrative discard operates on one previously registered open instance without saving. */
public final class CloseWorktreeService extends ManagedWorktreeService {
   @Override protected String getServiceName() { return "royder-close-worktree"; }

   @Override CompletableFuture<WorktreeReply> handle(String route, Path solution, String ifMatch) {
      var base = "/api/" + getServiceName();
      if (route.equals(base + "/projects")) {
         return onDispatchThread(() -> new WorktreeReply(
            ProjectManager.getInstance().getOpenProjects().length == 0 ? NO_CONTENT : OK, ""));
      }
      var discard = route.equals(base + "/discard");
      if (!discard && !route.equals(base)) return reply(NOT_FOUND, "Unknown operation");
      if (discard && ifMatch == null) return reply(PRECONDITION_REQUIRED, "Discard requires the open instance's If-Match tag");
      // Match only projects open at receipt, even if the worktree is reopened before EDT dispatch.
      var atReceipt = ProjectManager.getInstance().getOpenProjects();
      return onDispatchThread(() -> {
         var matches = OpenProjectInstance.at(solution, atReceipt);
         if (matches.isEmpty()) return new WorktreeReply(NOT_FOUND, "No open project for the solution");
         if (ifMatch != null) {
            matches = matches.stream().filter(project -> {
               var registered = OpenProjectInstance.of(project);
               return registered != null && ifMatch.equals(registered.etag());
            }).toList();
            if (matches.isEmpty()) return new WorktreeReply(PRECONDITION_FAILED, "This open instance no longer matches");
         }
         if (matches.size() != 1) return new WorktreeReply(CONFLICT, "Multiple open projects match the solution");
         var project = matches.get(0);
         if (discard) return new AbandonedRiderProject(project).discard();
         if (!Files.isRegularFile(solution)) return new WorktreeReply(CONFLICT, "Solution disappeared; discard the original open instance");
         var registered = OpenProjectInstance.of(project);
         if (registered != null) {
            try {
               if (!registered.disk().stillAt(solution)) return registered.replaced();
            } catch (IOException error) {
               return new WorktreeReply(CONFLICT, "Cannot verify the original worktree before saving", registered.etag());
            }
         }
         return ProjectManager.getInstance().closeAndDispose(project) ? new WorktreeReply(OK, "")
                                                                     : new WorktreeReply(CONFLICT, "Rider declined the close");
      });
   }
}
