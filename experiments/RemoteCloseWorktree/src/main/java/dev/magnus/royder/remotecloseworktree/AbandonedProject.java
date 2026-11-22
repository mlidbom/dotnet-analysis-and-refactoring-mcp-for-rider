package dev.magnus.royder.remotecloseworktree;

import static io.netty.handler.codec.http.HttpResponseStatus.*;

/** A project whose worktree the supervisor has lost. Its discard decision is made on Rider's dispatch thread. */
interface AbandonedProject {
   boolean hasUnsavedDocuments();
   boolean canClose();
   boolean isStillOpen();
   boolean disposeWithoutSaving();

   default WorktreeReply discard() {
      if (hasUnsavedDocuments()) return new WorktreeReply(CONFLICT, "The project has unsaved or unowned documents");
      if (!canClose()) return new WorktreeReply(CONFLICT, "Rider vetoed the close");
      // A close handler can run other work before returning its decision.
      if (!isStillOpen()) return new WorktreeReply(NOT_FOUND, "Project already closed");
      if (hasUnsavedDocuments()) return new WorktreeReply(CONFLICT, "Documents changed during the close check");
      return disposeWithoutSaving() ? new WorktreeReply(OK, "") : new WorktreeReply(CONFLICT, "Rider declined the close");
   }
}
