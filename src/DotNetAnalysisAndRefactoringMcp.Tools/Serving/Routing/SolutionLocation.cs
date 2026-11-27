#nullable enable
using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving.Routing;

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
      path.GitWorkTree != null ? FilePaths.Same(path.GitWorkTree, WorkTree) : FilePaths.IsUnder(path.Absolute, Folder);

   /// <summary>
   ///   How well it fits <paramref name="paths"/>, which it <see cref="Holds"/>, against the other solutions that hold
   ///   them. Two solutions in one work tree, such as a client's and a server's, then each fit their own files better
   ///   than the other does.
   /// </summary>
   public SolutionFit FitFor(PathsToRoute paths) => paths.FitOf(FilePath, WorkTree);

   /// <summary>Its name and path, as every listing of the solutions starts.</summary>
   public JObject NameAndPath() => new() { ["name"] = Name, ["path"] = FilePath };

   /// <summary>A path with slashes, between slashes, so that searching it for another finds only whole segments.</summary>
   static string Segmented(string path) => "/" + path.Replace('\\', '/') + "/";
}
