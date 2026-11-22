package dev.magnus.royder.remotecloseworktree;

import io.netty.handler.codec.http.HttpResponseStatus;
import static io.netty.handler.codec.http.HttpResponseStatus.*;

/** Probes what the management decision does across a veto callback; the private JVM fixture covers the actual disposal. */
public final class AbandonedProjectSpec implements AbandonedProject {
   boolean unsaved, veto, closedDuringCheck, changedDuringCheck, disposed;

   public static void main(String[] args) {
      var unsaved = new AbandonedProjectSpec();
      unsaved.unsaved = true;
      unsaved.expect(CONFLICT, false);
      var veto = new AbandonedProjectSpec();
      veto.veto = true;
      veto.expect(CONFLICT, false);
      var edited = new AbandonedProjectSpec();
      edited.changedDuringCheck = true;
      edited.expect(CONFLICT, false);
      var alreadyClosed = new AbandonedProjectSpec();
      alreadyClosed.closedDuringCheck = true;
      alreadyClosed.expect(NOT_FOUND, false);
      new AbandonedProjectSpec().expect(OK, true);
      System.out.println("Passed: dirty project, veto, edit or close during veto checks, clean discard.");
   }

   private void expect(HttpResponseStatus expected, boolean shouldDispose) {
      var answer = discard();
      if (!answer.status().equals(expected) || disposed != shouldDispose)
         throw new AssertionError("Expected " + expected + ", disposed=" + shouldDispose + "; got " + answer + ", disposed=" + disposed);
   }

   @Override public boolean hasUnsavedDocuments() { return unsaved; }
   @Override public boolean canClose() {
      unsaved = changedDuringCheck;
      return !veto;
   }
   @Override public boolean isStillOpen() { return !closedDuringCheck; }
   @Override public boolean disposeWithoutSaving() { disposed = true; return true; }
}
