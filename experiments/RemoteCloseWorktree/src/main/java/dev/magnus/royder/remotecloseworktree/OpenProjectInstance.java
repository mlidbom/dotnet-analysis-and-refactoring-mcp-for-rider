package dev.magnus.royder.remotecloseworktree;

import com.intellij.openapi.project.Project;
import com.jetbrains.rider.projectView.SolutionHostExtensionsKt;

import java.nio.file.Path;
import java.util.Arrays;
import java.util.List;
import java.util.Map;
import java.util.UUID;
import java.util.WeakHashMap;

import static io.netty.handler.codec.http.HttpResponseStatus.CONFLICT;

/** The registration follows the IntelliJ Project's lifetime, independent of the path on disk. Accessed on the EDT. */
record OpenProjectInstance(Path solution, WorktreeDiskIdentity disk, String etag) {
   private static final Map<Project, OpenProjectInstance> registered = new WeakHashMap<>();

   static List<Project> at(Path solution, Project[] candidates) {
      return Arrays.stream(candidates)
         .filter(project -> !project.isDisposed() && project.isOpen())
         .filter(project -> solution.equals(solutionOf(project)))
         .toList();
   }

   static OpenProjectInstance of(Project project) { return registered.get(project); }

   static WorktreeReply register(Project project, Path solution, WorktreeDiskIdentity disk) {
      if (project == null || project.isDisposed() || !project.isOpen())
         return new WorktreeReply(CONFLICT, "Rider did not open the solution");
      var knownPath = solutionOf(project);
      if (knownPath != null && !knownPath.equals(solution))
         return new WorktreeReply(CONFLICT, "Rider returned a project for a different solution");
      var instance = registered.computeIfAbsent(project, ignored ->
         new OpenProjectInstance(solution, disk, "\"" + UUID.randomUUID() + "\""));
      return instance.disk.equals(disk) ? WorktreeReply.opened(instance.etag) : instance.replaced();
   }

   WorktreeReply replaced() {
      return new WorktreeReply(CONFLICT, "Worktree replaced; discard the previous open instance", etag);
   }

   private static Path solutionOf(Project project) {
      var instance = of(project);
      if (instance != null) return instance.solution;
      if (!SolutionHostExtensionsKt.getHasSolution(project) || !SolutionHostExtensionsKt.isExistingSolution(project)) return null;
      return Path.of(SolutionHostExtensionsKt.getSolutionPath(project)).normalize();
   }
}
