package dev.magnus.royder.remotecloseworktree;

import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.BasicFileAttributes;
import java.util.concurrent.TimeUnit;
import java.util.regex.Pattern;

import static java.nio.charset.StandardCharsets.US_ASCII;
import static java.nio.file.LinkOption.NOFOLLOW_LINKS;

/** The solution directory and its Git registration marker can each be replaced at the same path. */
record WorktreeDiskIdentity(String directoryId, Path gitMarker, String markerId) {
   private static final Pattern WINDOWS_FILE_ID = Pattern.compile("(?i)\\b0x[0-9a-f]{16,32}\\b");

   static WorktreeDiskIdentity read(Path solution) throws IOException {
      var directory = solution.getParent();
      var directoryId = fileId(directory);
      for (var parent = directory; parent != null; parent = parent.getParent()) {
         var marker = parent.resolve(".git");
         if (Files.exists(marker, NOFOLLOW_LINKS)) return new WorktreeDiskIdentity(directoryId, marker, fileId(marker));
      }
      return new WorktreeDiskIdentity(directoryId, null, null);
   }

   boolean stillAt(Path solution) throws IOException {
      return Files.isRegularFile(solution, NOFOLLOW_LINKS) && equals(read(solution));
   }

   private static String fileId(Path path) throws IOException {
      var key = Files.readAttributes(path, BasicFileAttributes.class, NOFOLLOW_LINKS).fileKey();
      if (key != null) return key.toString();

      // Windows NIO in this Rider runtime returns null for fileKey. fsutil reads NTFS's ID without elevation
      // or keeping a directory handle across requests; the volume serial qualifies the ID across mount points.
      // On 2026-11-24 two opens failed with the identity unread, while the JVM processed a storm of VFS events; the
      // cause wasn't logged, and fsutil outlasting the 3 s it had then is the likely one. So it gets 20 s, and a
      // second try; OpenWorktreeService now logs why it failed.
      try { return queriedFileId(path); }
      catch (IOException first) { return queriedFileId(path); }
   }

   private static String queriedFileId(Path path) throws IOException {
      var systemRoot = System.getenv("SystemRoot");
      if (systemRoot == null) throw new IOException("No filesystem identity available for " + path);
      var volume = Files.getFileStore(path).getAttribute("volume:vsn");
      var fsutil = Path.of(systemRoot, "System32", "fsutil.exe");
      var query = new ProcessBuilder(fsutil.toString(), "file", "queryfileid", path.toString())
         .directory(fsutil.getParent().toFile()).redirectErrorStream(true).start();
      try (var output = query.getInputStream()) {
         if (!query.waitFor(20, TimeUnit.SECONDS)) throw new IOException("Filesystem identity query timed out for " + path);
         var id = WINDOWS_FILE_ID.matcher(new String(output.readAllBytes(), US_ASCII));
         if (query.exitValue() != 0 || !id.find()) throw new IOException("Cannot identify " + path);
         return volume + "/" + id.group();
      } catch (InterruptedException interrupted) {
         Thread.currentThread().interrupt();
         throw new IOException("Filesystem identity query interrupted", interrupted);
      } finally {
         if (query.isAlive()) query.destroyForcibly();
      }
   }
}
