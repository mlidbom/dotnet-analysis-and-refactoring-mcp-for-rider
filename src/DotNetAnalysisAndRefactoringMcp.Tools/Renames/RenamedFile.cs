#nullable enable
using System.IO;
using JetBrains.Util;

namespace DotNetAnalysisAndRefactoringMcp.Renames;

/// <summary>
///   A file that a rename renamed, as Rider's Rename renames a type's file named after it: its full path before, and
///   after.
/// </summary>
sealed class RenamedFile
{
   /// <summary>The file at <paramref name="location"/>, renamed to <paramref name="newName"/> in its folder.</summary>
   public RenamedFile(VirtualFileSystemPath location, string newName)
   {
      From = location.FullPath;
      To = location.Directory.Combine(newName).FullPath;
   }

   public string From { get; }
   public string To { get; }

   /// <summary>
   ///   Its line in an answer: its new path relative to <paramref name="root"/>, and the name it had before:
   ///   <c>Shapes\Disk.cs, renamed from Circle.cs</c>.
   /// </summary>
   public string LineIn(SolutionRoot root) => $"{root.Relative(To)}, renamed from {Path.GetFileName(From)}";
}
