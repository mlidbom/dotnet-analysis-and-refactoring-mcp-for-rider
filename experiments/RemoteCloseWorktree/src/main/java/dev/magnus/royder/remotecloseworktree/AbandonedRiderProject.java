package dev.magnus.royder.remotecloseworktree;

import com.intellij.openapi.fileEditor.FileDocumentManager;
import com.intellij.openapi.fileEditor.FileEditorManager;
import com.intellij.openapi.project.Project;
import com.intellij.openapi.project.ProjectLocator;
import com.intellij.openapi.project.ex.ProjectManagerEx;

record AbandonedRiderProject(Project project) implements AbandonedProject {
   @Override public boolean isStillOpen() { return !project.isDisposed() && project.isOpen(); }
   @Override public boolean canClose() { return ProjectManagerEx.getInstanceEx().canClose(project); }
   @Override public boolean disposeWithoutSaving() { return ProjectManagerEx.getInstanceEx().forceCloseProject(project, false); }

   @Override public boolean hasUnsavedDocuments() {
      var documents = FileDocumentManager.getInstance();
      for (var document : documents.getUnsavedDocuments()) {
         var file = documents.getFile(document);
         if (file == null || !file.isValid() || FileEditorManager.getInstance(project).isFileOpen(file)) return true;
         var owners = ProjectLocator.getInstance().getProjectsForFile(file);
         if (owners.isEmpty() || owners.contains(project)) return true;
      }
      return false;
   }
}
