using System;
using System.IO;
using System.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Disk;

/// <summary>
///   A file's text on disk, as compared with Rider's copy of it, a document's text: line endings and a byte order mark
///   are representation, not content, and Rider may normalize either.
/// </summary>
static class DiskText
{
   /// <summary>Whether <paramref name="file"/> holds <paramref name="text"/> on disk.</summary>
   /// <remarks>
   ///   A file that can't be read, such as one another process is writing, or one that isn't there, can't be shown to
   ///   hold it, so it doesn't: the disk sync then waits for it, and edit tools are refused while it stays unreadable.
   ///   Until 2026-11-15 an unreadable file counted as holding it, and an edit went ahead on what may have been Rider's
   ///   old copy.
   /// </remarks>
   public static bool Holds(string file, string text)
   {
      try
      {
         return Normalized(text) == Normalized(File.ReadAllText(file));
      }
      catch(IOException)
      {
         return false;
      }
   }

   /// <summary>
   ///   How <paramref name="file"/> on disk differs from <paramref name="text"/>, for a note: the first line in which they
   ///   differ, as each has it, or why the file can't be read. Null when it holds the text.
   /// </summary>
   public static string DifferenceOf(string file, string text)
   {
      string onDisk;
      try
      {
         onDisk = Normalized(File.ReadAllText(file));
      }
      catch(IOException exception)
      {
         return $"the file can't be read: {exception.Message}";
      }

      var inRider = Normalized(text);
      if(onDisk == inRider) return null;

      var diskLines = onDisk.Split('\n');
      var riderLines = inRider.Split('\n');
      var line = Enumerable.Range(0, Math.Max(diskLines.Length, riderLines.Length)).First(it => LineOf(diskLines, it) != LineOf(riderLines, it));
      return $"from line {line + 1}: {Quoted(LineOf(diskLines, line))} on disk, {Quoted(LineOf(riderLines, line))} in Rider";

      static string LineOf(string[] lines, int index) => index < lines.Length ? lines[index] : null;
      static string Quoted(string line) => line == null ? "no line" : $"\"{(line.Length > 80 ? line.Substring(0, 80) + "\u2026" : line)}\"";
   }

   static string Normalized(string text) => text.TrimStart('\uFEFF').Replace("\r\n", "\n");
}
