using System;
using System.Collections.Generic;
using System.IO;
using DotNetAnalysisAndRefactoringMcp.Protocol;
using DotNetAnalysisAndRefactoringMcp.Serving.Routing;
using Newtonsoft.Json.Linq;

namespace DotNetAnalysisAndRefactoringMcp.Serving;

/// <summary>
///   A solution open in a Rider on this machine, this one's (<see cref="LocalSolution"/>) or a peer's
///   (<see cref="PeerSolution"/>): what a <c>tools/call</c> is routed to, by its name or by the files the call names.
/// </summary>
abstract class OpenSolution(string name, string filePath) : SolutionLocation(name, filePath)
{
   SolutionRoot _root;
   SolutionRoot Root => _root ??= SolutionRoot.Of(Folder);
   public override string WorkTree => Root.Workspace;
   public override string Absolute(string path) => Root.Absolute(path);
   public override int ToolCount => Tools.Count;

   /// <summary>The tools it is served with, as <c>tools/list</c> describes them.</summary>
   public abstract IReadOnlyList<ToolDefinition> Tools { get; }

   /// <summary>The answer to <paramref name="request"/>: the call of <paramref name="tool"/> with <paramref name="arguments"/>.</summary>
   public abstract JsonRpcResponse Call(JsonRpcRequest request, string tool, JObject arguments);

   /// <summary>What <c>/health</c> says of it: whether it is indexing, the disk changes not synced yet, the tools builds.</summary>
   public abstract JObject Health();
}
