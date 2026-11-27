#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving.Routing;

/// <summary>
///   The folder of the project a file or folder is in, found by where it lies: the nearest folder at or above it that
///   holds a project file, such as a <c>.csproj</c>, looking no higher than the root of its Git work tree.
/// </summary>
/// <remarks>
///   An SDK-style project takes in every file under its folder, so this finds the project of all of them, but for a file
///   a project links from elsewhere, as <c>Royder.Primary</c> links the routing's files.
/// </remarks>
sealed class ProjectFolder
{
   ProjectFolder(IReadOnlyList<string> projectFiles) => ProjectFiles = projectFiles;

   /// <summary>The project files in the folder, by their absolute paths: one, as a rule.</summary>
   public IReadOnlyList<string> ProjectFiles { get; }

   /// <summary>
   ///   The project folder of the absolute <paramref name="path"/>, which need not exist yet, or none when no folder up
   ///   to its work tree's root holds a project file.
   /// </summary>
   public static ProjectFolder? Of(string path)
   {
      for(var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path); folder != null; folder = Path.GetDirectoryName(folder))
      {
         if(!Directory.Exists(folder)) continue;

         var projectFiles = Directory.GetFiles(folder).Where(IsProjectFile).ToList();
         if(projectFiles.Count > 0) return new ProjectFolder(projectFiles);
         if(RequestedPath.IsWorkTreeRoot(folder)) return null;
      }

      return null;
   }

   /// <summary>
   ///   Whether <paramref name="path"/> names a project file: a <c>.csproj</c>, <c>.fsproj</c>, <c>.vbproj</c>, or another of
   ///   the kinds whose extension ends in <c>proj</c>.
   /// </summary>
   public static bool IsProjectFile(string path) => Path.GetExtension(path).EndsWith("proj", StringComparison.OrdinalIgnoreCase);
}
