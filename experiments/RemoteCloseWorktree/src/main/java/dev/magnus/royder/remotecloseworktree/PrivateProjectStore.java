package dev.magnus.royder.remotecloseworktree;

import com.intellij.ide.impl.OpenProjectTaskBuilder;
import com.intellij.ide.trustedProjects.TrustedProjects;
import com.intellij.openapi.application.ApplicationManager;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.project.ex.ProjectManagerEx;
import com.intellij.openapi.vfs.VirtualFile;
import com.jetbrains.rd.ide.model.RdOpenSolution;
import com.jetbrains.rd.platform.ProjectUtilKt;
import com.jetbrains.rider.projectView.SolutionDescriptionFactory;
import com.jetbrains.rider.projectView.SolutionInitializerService;
import com.jetbrains.rider.services.security.TrustedSolutionManager;
import kotlin.Unit;
import kotlin.coroutines.EmptyCoroutineContext;
import kotlinx.coroutines.BuildersKt;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;

/** An IntelliJ project in the managed host's private state, with a ReSharper backend for the real solution. */
final class PrivateProjectStore {
   private final Path root;

   private PrivateProjectStore(Path root) { this.root = root; }

   static PrivateProjectStore configured() {
      var directory = System.getenv("ROYDER_PRIVATE_PROJECT_STORES");
      return directory == null ? null : new PrivateProjectStore(Path.of(directory));
   }

   boolean contains(Project project) {
      var file = project.getProjectFilePath();
      return file != null && Path.of(file).normalize().startsWith(root);
   }

   Project open(Path solution, VirtualFile file, String displayName, Path trustedIdeaDirectory, WorktreeDiskIdentity disk)
      throws IOException, InterruptedException {
      var checkoutRoot = disk.gitMarker() == null ? solution.getParent() : disk.gitMarker().getParent();
      if (!root.isAbsolute() || !root.toRealPath().equals(root) || root.startsWith(checkoutRoot) || checkoutRoot.startsWith(root))
         throw new IOException("Project store must be a private physical directory outside the checkout");

      var description = SolutionDescriptionFactory.INSTANCE.existing(file.getPath(), displayName);
      var trusted = ManagedWorktreeService.onDispatchThread(() -> {
         var manager = TrustedSolutionManager.INSTANCE;
         var additional = manager.findUntrustedAncestorConfigFiles(solution.getParent());
         return additional.isEmpty() && manager.isTrusted(List.of(trustedIdeaDirectory), solution.getParent(), description, additional);
      }).join();
      if (!trusted || !disk.stillAt(solution)) return null;

      var projectDir = Files.createTempDirectory(root, "project-");
      var strategy = new RdOpenSolution(description, true);
      var options = new OpenProjectTaskBuilder();
      options.setForceOpenInNewFrame(true);
      options.setNewProject(true);
      options.setUseDefaultProjectAsTemplate(true);
      options.setProjectName(displayName);
      options.setBeforeInitTasks(List.of(project -> {
         project.putUserData(ProjectUtilKt.getRdEarlyProjectDir(), projectDir.toString());
         ApplicationManager.getApplication().getService(SolutionInitializerService.class).initSolution(project, strategy);
         return Unit.INSTANCE;
      }));
      var project = BuildersKt.<Project>runBlocking(EmptyCoroutineContext.INSTANCE, (scope, continuation) ->
         ProjectManagerEx.getInstanceEx().openProjectAsync(projectDir, options.build(), continuation));
      if (project != null) {
         if (!contains(project)) throw new IllegalStateException("Rider did not select the private project store");
         TrustedProjects.setProjectTrusted(project, true);
      }
      return project;
   }
}
