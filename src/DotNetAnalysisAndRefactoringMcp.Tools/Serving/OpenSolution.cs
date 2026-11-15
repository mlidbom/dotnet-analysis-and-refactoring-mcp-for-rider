using System;
using System.Collections.Generic;
using System.IO;
using DotNetAnalysisAndRefactoringMcp.Protocol;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>
///   A solution open in a Rider on this machine, this one's (<see cref="LocalSolution"/>) or a peer's
///   (<see cref="PeerSolution"/>): what a <c>tools/call</c> is routed to, by its name or by the files the call names.
/// </summary>
abstract class OpenSolution(string name, string filePath)
{
   /// <summary>The solution file's name without its extension.</summary>
   public string Name { get; } = name;

   /// <summary>The solution file's full path.</summary>
   public string FilePath { get; } = filePath;

   /// <summary>
   ///   The work tree the solution is in, not the solution file's folder: a solution in <c>Client/TPS</c> still owns
   ///   files under <c>Shared/</c>.
   /// </summary>
   public string WorkTree => SolutionRoot.Of(Path.GetDirectoryName(FilePath)).Workspace ?? FilePath;

   /// <summary>The tools it is served with, as <c>tools/list</c> describes them.</summary>
   public abstract IReadOnlyList<ToolDefinition> Tools { get; }

   /// <summary>The answer to <paramref name="request"/>: the call of <paramref name="tool"/> with <paramref name="arguments"/>.</summary>
   public abstract JsonRpcResponse Call(JsonRpcRequest request, string tool, JObject arguments);

   /// <summary>What <c>/health</c> says of it: whether it is indexing, the disk changes not synced yet, the tools builds.</summary>
   public abstract JObject Health();

   /// <summary>Whether <paramref name="solutionName"/> is its name or its file's path, in any case.</summary>
   public bool IsNamed(string solutionName) =>
      Name.Equals(solutionName, StringComparison.OrdinalIgnoreCase) || FilePath.Equals(solutionName, StringComparison.OrdinalIgnoreCase);

   /// <summary>
   ///   Whether <paramref name="segment"/> is whole segments of its file's path: <c>tps-project</c> is one of
   ///   <c>.../tps-project/...</c> but not of <c>.../tps-project-dyn/...</c>, and <c>tps-project/Client</c> is two.
   /// </summary>
   public bool HasPathSegment(string segment) => Segmented(FilePath).IndexOf(Segmented(segment), StringComparison.OrdinalIgnoreCase) >= 0;

   /// <summary>Whether <paramref name="path"/> is in it: an absolute path under its work tree, a relative one that exists in it.</summary>
   public bool Holds(string path)
   {
      if(Path.IsPathRooted(path))
         return path.Replace('\\', '/').StartsWith(WorkTree.Replace('\\', '/').TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);

      var absolute = SolutionRoot.Of(Path.GetDirectoryName(FilePath)).Absolute(path);

      return absolute != null && (File.Exists(absolute) || Directory.Exists(absolute));
   }

   /// <summary>Its name and path, as every listing of the solutions starts.</summary>
   public JObject NameAndPath() => new() { ["name"] = Name, ["path"] = FilePath };

   /// <summary>A path with slashes, between slashes, so that searching it for another finds only whole segments.</summary>
   static string Segmented(string path) => "/" + path.Replace('\\', '/') + "/";
}
