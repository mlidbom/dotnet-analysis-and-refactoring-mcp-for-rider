package dev.magnus.royder.remotecloseworktree;

import java.nio.file.Files;
import java.nio.file.Path;

/** Uses a disposable on-disk fixture supplied by Build.ps1. No IDE or server starts. */
public final class WorktreeDiskIdentitySpec {
   public static void main(String[] args) throws Exception {
      var root = Files.createDirectory(Path.of(args[0], "worktree"));
      var marker = Files.writeString(root.resolve(".git"), "gitdir: ../repo/worktrees/fixture");
      var nested = Files.createDirectory(root.resolve("src"));
      var solution = Files.writeString(nested.resolve("Fixture.slnx"), "<Solution />");
      var original = WorktreeDiskIdentity.read(solution);
      Files.delete(solution);
      Files.writeString(solution, "<Solution> </Solution>");
      require(original.equals(WorktreeDiskIdentity.read(solution)), "An ordinary solution edit changed worktree identity");

      Files.move(marker, marker.resolveSibling(".previous-registration"));
      Files.writeString(marker, "gitdir: ../repo/worktrees/fixture");
      var reRegistered = WorktreeDiskIdentity.read(solution);
      require(!original.equals(reRegistered), "A replacement Git marker at the same path had the original identity");

      Files.move(nested, root.resolve("previous-src"));
      Files.createDirectory(nested);
      Files.writeString(solution, "<Solution> </Solution>");
      require(!reRegistered.equals(WorktreeDiskIdentity.read(solution)), "A replacement directory had the original identity");
      System.out.println("Passed: solution rewrite, same-path Git registration replacement, same-path directory replacement.");
   }

   private static void require(boolean condition, String failure) {
      if (!condition) throw new AssertionError(failure);
   }
}
