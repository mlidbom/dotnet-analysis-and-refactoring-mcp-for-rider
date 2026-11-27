#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving.Routing;

/// <summary>A solution file, a <c>.sln</c> or an <c>.slnx</c>, as routing reads it: the projects it lists.</summary>
sealed class SolutionFile
{
   /// <summary>A project's line in a <c>.sln</c>: <c>Project("{type}") = "Name", "Folder\Name.csproj", "{id}"</c>.</summary>
   static readonly Regex SlnProjectLine = new(@"^Project\(""\{[^}]*\}""\)\s*=\s*""[^""]*""\s*,\s*""(?<path>[^""]+)""", RegexOptions.Multiline);

   readonly HashSet<string> _projectFiles;

   SolutionFile(IEnumerable<string> projectFiles) => _projectFiles = new HashSet<string>(projectFiles, FilePaths.Comparer);

   /// <summary>How many projects it lists.</summary>
   public int ProjectCount => _projectFiles.Count;

   /// <summary>Whether it lists a project file of <paramref name="project"/>.</summary>
   public bool Lists(ProjectFolder project) => project.ProjectFiles.Any(_projectFiles.Contains);

   /// <summary>The solution file at the absolute <paramref name="path"/>, as it is on disk now.</summary>
   public static SolutionFile Read(string path)
   {
      var folder = Path.GetDirectoryName(path)!;
      var text = FilePaths.TextOf(path);
      var listed = Path.GetExtension(path).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
                      ? XDocument.Parse(text).Descendants().Where(it => it.Name.LocalName == "Project").Select(it => (string?)it.Attribute("Path"))
                      : SlnProjectLine.Matches(text).Cast<Match>().Select(it => it.Groups["path"].Value);
      // A .sln lists its solution folders as projects too, named and not a file.
      return new(listed.OfType<string>().Where(ProjectFolder.IsProjectFile).Select(it => Path.GetFullPath(Path.Combine(folder, it))));
   }
}
