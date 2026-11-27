#nullable enable
using System;
using System.IO;

namespace DotNetAnalysisAndRefactoringMcp.Serving.Routing;

/// <summary>
///   Paths as routing compares them: whatever their slashes, and in any case on Windows. And the files it reads, read
///   without keeping anyone from writing them.
/// </summary>
static class FilePaths
{
   static readonly bool CaseInsensitive = Path.DirectorySeparatorChar == '\\';

   public static readonly StringComparison Comparison = CaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

   public static readonly StringComparer Comparer = CaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

   /// <summary>Whether two absolute paths name the same file or folder.</summary>
   public static bool Same(string first, string second) => string.Equals(Normalized(first), Normalized(second), Comparison);

   /// <summary>Whether the absolute <paramref name="path"/> is <paramref name="folder"/> or lies under it.</summary>
   public static bool IsUnder(string path, string folder) => (Normalized(path) + "/").StartsWith(Normalized(folder) + "/", Comparison);

   /// <summary>
   ///   The text of <paramref name="file"/>, read while Rider may write it: a handle that shares only reading made
   ///   Rider's saves fail (2026-11-27, the disk sync's <c>DiskText.Of</c>).
   /// </summary>
   public static string TextOf(string file)
   {
      using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
      using var reader = new StreamReader(stream);
      return reader.ReadToEnd();
   }

   static string Normalized(string path) => path.Replace('\\', '/').TrimEnd('/');
}
