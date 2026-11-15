using System.IO;

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

   static string Normalized(string text) => text.TrimStart('\uFEFF').Replace("\r\n", "\n");
}
