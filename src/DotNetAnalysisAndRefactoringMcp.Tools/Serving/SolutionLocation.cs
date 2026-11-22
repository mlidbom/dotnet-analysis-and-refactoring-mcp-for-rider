#nullable enable
using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>A solution's address in the tool catalog and the worktree its file arguments belong to.</summary>
abstract class SolutionLocation(string name, string filePath)
{
   public string Name { get; } = name;
   public string FilePath { get; } = filePath;
   protected string Folder => Path.GetDirectoryName(FilePath)!;
   public abstract string WorkTree { get; }
   public abstract string Absolute(string path);
   public abstract int ToolCount { get; }

   /// <summary>Whether <paramref name="solutionName"/> is its name or its file's path, in any case.</summary>
   public bool IsNamed(string solutionName) =>
      Name.Equals(solutionName, StringComparison.OrdinalIgnoreCase) || FilePath.Equals(solutionName, StringComparison.OrdinalIgnoreCase);

   /// <summary>
   ///   Whether <paramref name="segment"/> is whole segments of its file's path: <c>tps-project</c> is one of
   ///   <c>.../tps-project/...</c> but not of <c>.../tps-project-dyn/...</c>, and <c>tps-project/Client</c> is two.
   /// </summary>
   public bool HasPathSegment(string segment) => Segmented(FilePath).IndexOf(Segmented(segment), StringComparison.OrdinalIgnoreCase) >= 0;

   /// <summary>Whether this solution owns the path's Git work tree. Outside Git, its own folder defines its reach.</summary>
   public bool Holds(RequestedPath path) =>
      path.GitWorkTree != null
         ? string.Equals(path.GitWorkTree.TrimEnd(Path.DirectorySeparatorChar), WorkTree.TrimEnd(Path.DirectorySeparatorChar), PathComparison)
         : IsUnder(path.Absolute, Folder);

   /// <summary>
   ///   How closely it holds the absolute <paramref name="path"/>, which it <see cref="Holds"/>: by its own folder, when
   ///   the path is under it, more closely than by the work tree around it, and by a deeper folder more closely than by
   ///   a shallower one. Two solutions in one work tree, such as a client's and a server's, then each hold their own
   ///   files more closely than the other does. A relative path is held by none more closely than by another.
   /// </summary>
   public int Closeness(string path)
   {
      if(!Path.IsPathRooted(path)) return 0;
      return IsUnder(Path.GetFullPath(path), Folder) ? Folder.Length : WorkTree.Length;
   }

   static readonly StringComparison PathComparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

   static bool IsUnder(string path, string folder) =>
      (path.Replace('\\', '/').TrimEnd('/') + "/").StartsWith(folder.Replace('\\', '/').TrimEnd('/') + "/", PathComparison);

   /// <summary>Its name and path, as every listing of the solutions starts.</summary>
   public JObject NameAndPath() => new() { ["name"] = Name, ["path"] = FilePath };

   /// <summary>A path with slashes, between slashes, so that searching it for another finds only whole segments.</summary>
   static string Segmented(string path) => "/" + path.Replace('\\', '/') + "/";
}
