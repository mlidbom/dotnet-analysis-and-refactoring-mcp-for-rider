using DotNetAnalysisAndRefactoringMcp.Serving;
using System.Net.Sockets;
using DotNetAnalysisAndRefactoringMcp.Tools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Royder.Primary.Worktrees;

namespace Royder.Primary.Transport;

/// <summary>Answers MCP discovery without an IDE and dispatches each accepted tool call once to its owning solution.</summary>
sealed class PrimaryRequests(PrimarySettings settings, OrdinaryRiders ordinary, ManagedWorktrees worktrees,
                             RiderEndpoint backend, PrimaryToolCatalog catalog, ToolCatalog pinned, ILogger<PrimaryRequests> log)
{
   public async Task<JObject?> Answer(JObject request, CancellationToken cancel)
   {
      var id = request["id"];
      if(id == null || id.Type == JTokenType.Null) return null;
      var parameters = request["params"] as JObject ?? new();
      try
      {
         switch((string?)request["method"])
         {
            case "initialize":
               var result = (JObject)pinned.Initialize.DeepClone();
               result["protocolVersion"] = (string?)parameters["protocolVersion"] == "2024-11-05" ? "2024-11-05" : "2025-03-26";
               result["instructions"] = PrimaryToolCatalog.Instructions;
               return Rpc.Result(id, result);
            case "ping": return Rpc.Result(id, new JObject());
            case "tools/list": return Rpc.Result(id, new JObject { ["tools"] = await catalog.Listed(cancel) });
            case "tools/call": return await Call(id, parameters, cancel);
            case "internal/status":
               var health = await Health(cancel);
               return Rpc.Result(id, new JObject { ["role"] = "primary", ["port"] = settings.Port, ["solutions"] = health["solutions"] });
            default: return Rpc.Error(id, -32601, $"Unknown method '{request["method"]}'.");
         }
      }
      catch(Exception ex) when(!cancel.IsCancellationRequested)
      {
         return (string?)request["method"] == "tools/call" ? Rpc.ToolResult(id, ex.GetBaseException().Message, true) : Rpc.Error(id, -32603, ex.GetBaseException().Message);
      }
   }

   async Task<JObject> Call(JToken id, JObject parameters, CancellationToken cancel)
   {
      var deadline = new AnswerDeadline(TimeSpan.FromSeconds(settings.AnswerWithinSeconds));
      var tool = (string?)parameters["name"] ?? throw new ArgumentException("A tool name is required.");
      var arguments = parameters["arguments"] as JObject ?? new();
      var managementPath = tool is PrimaryToolCatalog.Close or PrimaryToolCatalog.Warm ? AbsoluteSolution((string?)arguments["solutionPath"]) : null;
      var managementTarget = managementPath == null ? null : worktrees.Registration(managementPath);
      var desktop = await ordinary.Solutions(cancel);
      var managed = worktrees.Open.Select(it => new ServingSolution(Path.GetFileNameWithoutExtension(it.SolutionPath), it.SolutionPath,
         pinned.Tools.Count - 1, backend, true));
      var all = desktop.Concat(managed).DistinctBy(it => it.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
      var routing = new SolutionRouting<ServingSolution>(all);
      if(tool == "list_solutions")
      {
         var listed = routing.Listed();
         listed["hint"] = PrimaryToolCatalog.OpenOnDemand;
         return Rpc.ToolResult(id, listed.ToString(Formatting.Indented));
      }
      if(tool is PrimaryToolCatalog.Warm or PrimaryToolCatalog.Close)
      {
         var path = managementPath ?? throw new ArgumentException("Expected an absolute solutionPath.");
         if(desktop.FirstOrDefault(it => RegisteredWorktree.SamePath(it.FilePath, path)) is {} inEditor)
         {
            await worktrees.YieldToOrdinaryRider(inEditor.WorkTree, cancel);
            if(tool == PrimaryToolCatalog.Close) throw new InvalidOperationException("This solution belongs to an ordinary Rider.");
            return Rpc.ToolResult(id, $"Already open in ordinary Rider: {path}");
         }
         if(managementTarget == null && tool == PrimaryToolCatalog.Close)
            return await ClosedByTheDeadline(id, path, worktrees.CloseUnregistered(path, CancellationToken.None), deadline, cancel);
         var target = managementTarget ?? throw new InvalidOperationException("Expected a configured main checkout or one of its linked worktrees.");
         if(desktop.Any(it => RegisteredWorktree.SamePath(it.WorkTree, target.Root)))
         {
            await worktrees.YieldToOrdinaryRider(target.Root, cancel);
            throw new InvalidOperationException($"An ordinary Rider owns this worktree: {target.Root}");
         }
         if(tool == PrimaryToolCatalog.Close)
            return await ClosedByTheDeadline(id, path, worktrees.CloseRequested(target, path, false, CancellationToken.None), deadline, cancel);
         var solution = await target.SolutionFor([], path, cancel);
         using var lease = await worktrees.Use(target, solution, deadline, cancel);
         return Rpc.ToolResult(id, $"Ready: {path}");
      }
      var paths = FileArguments.Of(arguments);
      var selector = (string?)arguments["solutionName"];
      var (chosen, refusal) = routing.Choose(selector, paths);
      if(chosen != null && !chosen.Managed)
      {
         await worktrees.YieldToOrdinaryRider(chosen.WorkTree, cancel);
         return await AnsweredByTheDeadline(Forward(chosen.Endpoint, chosen.FilePath));
      }
      if(!await catalog.Provides(tool, cancel))
         return Rpc.ToolResult(id, $"No tools build Royder serves provides '{tool}'.", true);
      var explicitPath = AbsoluteSolution(selector);
      if(chosen == null && selector != null && explicitPath == null) return Rpc.ToolResult(id, $"{refusal} {PrimaryToolCatalog.OpenOnDemand}", true);
      var absolutePaths = paths.Where(Path.IsPathFullyQualified).Select(Path.GetFullPath).ToArray();
      var anchor = chosen?.FilePath ?? explicitPath ?? absolutePaths.FirstOrDefault();
      var registration = anchor == null ? null : worktrees.Registration(anchor);
      if(registration == null) return Rpc.ToolResult(id, anchor == null ? $"{refusal} {PrimaryToolCatalog.OpenOnDemand}" :
         chosen == null ? $"{refusal} {NotTrusted(anchor)}" : "The managed worktree is no longer registered.", true);
      if(desktop.Any(it => RegisteredWorktree.SamePath(it.WorkTree, registration.Root)))
      {
         await worktrees.YieldToOrdinaryRider(registration.Root, cancel);
         throw new InvalidOperationException($"An ordinary Rider owns this worktree. Select its open solution: {registration.Root}");
      }
      if(selector == null && absolutePaths.Any(path => worktrees.Registration(path) != registration))
         return Rpc.ToolResult(id, "The files belong to different worktrees. Send one worktree per call.", true);
      var solutionPath = chosen?.FilePath ?? await registration.SolutionFor(absolutePaths, explicitPath, cancel);
      if(selector == null)
      {
         var candidate = new ServingSolution(Path.GetFileNameWithoutExtension(solutionPath), solutionPath, 0, backend, true);
         if(paths.Any(path => !candidate.Holds(new RequestedPath(candidate.Absolute(path)))))
            return Rpc.ToolResult(id, "The named files do not all belong to the selected worktree.", true);
      }
      var use = await worktrees.Use(registration, solutionPath, deadline, cancel);
      return await AnsweredByTheDeadline(ForwardToThePrivateRider());

      async Task<JObject> ForwardToThePrivateRider()
      {
         // Held until the private Rider answers, also when the deadline answered the agent before then.
         using(use)
         {
            if(worktrees.Registration(solutionPath) != registration) throw new InvalidOperationException("The worktree was replaced before dispatch.");
            try
            {
               var reply = await Forward(backend, solutionPath);
               if((bool?)reply.SelectToken("result.isError") == true)
               {
                  var health = await backend.Health(CancellationToken.None);
                  var row = (health?["solutions"] as JArray)?.FirstOrDefault(it => RegisteredWorktree.SamePath((string?)it["path"], solutionPath));
                  if(row == null || (bool?)row["reachable"] == false) use.Invalidate(false);
               }
               return reply;
            }
            catch(HttpRequestException ex)
            {
               use.Invalidate(ex.GetBaseException() is not SocketException { SocketErrorCode: SocketError.ConnectionRefused });
               throw;
            }
            catch(OperationCanceledException ex) when(ex.CancellationToken != cancel) { use.Invalidate(true); throw; }
         }
      }

      async Task<JObject> AnsweredByTheDeadline(Task<JObject> forwarded)
      {
         try { return await deadline.Awaited(forwarded, cancel); }
         catch(TimeoutException) when(!forwarded.IsCompleted)
         {
            return Rpc.ToolResult(id, $"Rider has not answered within {deadline.Within.TotalSeconds:0} seconds, and MCP clients give up soon after. "
                                      + "The call goes on in Rider: retry a lookup in a minute; an edit still lands, so check its files before repeating it.", true);
         }
      }

      async Task<JObject> Forward(RiderEndpoint endpoint, string solution)
      {
         cancel.ThrowIfCancellationRequested();
         var routed = (JObject)arguments.DeepClone();
         routed["solutionName"] = solution;
         var call = Rpc.Request("tools/call", new JObject { ["name"] = tool, ["arguments"] = routed });
         call["id"] = id;
         // After dispatch the lease stays held even if the MCP client disconnects. An edit is never replayed.
         return await endpoint.Send(call, CancellationToken.None);
      }

      string NotTrusted(string path) =>
         $"Royder opens on demand only the checkouts of the repositories its settings trust, and {path} is in none of them: {settings.Trusted.Described}.";
   }

   /// <summary>
   ///   The answer to <c>close_worktree</c> for <paramref name="path"/>: closed, once Rider's backend for it and the
   ///   processes that backend started have exited, or nothing to close, or, by the deadline, still closing.
   /// </summary>
   /// <remarks>
   ///   <paramref name="closing"/> was started without the request's cancellation: once asked for, a close goes on whether
   ///   the client waits or not, since Rider may already be saving the project. Until 2026-11-25 the call waited for the
   ///   close however long it took, and agents were told only "The operation timed out." by their MCP client after 60
   ///   seconds, of closes that had in fact freed the folder: they could not tell whether to remove it.
   /// </remarks>
   async Task<JObject> ClosedByTheDeadline(JToken id, string path, Task<bool> closing, AnswerDeadline deadline, CancellationToken cancel)
   {
      try
      {
         return Rpc.ToolResult(id, await deadline.Awaited(closing, cancel)
                                      ? $"Closed: {path}. Its Rider backend and the processes that backend started have exited."
                                      : $"Nothing to close: Royder has no project open for {path}");
      }
      catch(TimeoutException) when(!closing.IsCompleted)
      {
         _ = closing.ContinueWith(it => log.LogWarning("Closing {Path}, which close_worktree had answered was still closing, failed: {Reason}",
                                                       path, it.Exception!.GetBaseException().Message),
                                  CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
         return Rpc.ToolResult(id, $"Still closing {path} after {deadline.Within.TotalSeconds:0} seconds, and MCP clients give up soon after. "
                                   + "The close goes on: call close_worktree again, which answers Closed once Rider's backend for it "
                                   + "and the processes that backend started have exited.");
      }
   }

   public async Task<JObject> Health(CancellationToken cancel)
   {
      var desktop = await ordinary.Health(cancel);
      var opening = worktrees.Opening;
      var managed = worktrees.Open.Length == 0 && opening.Length == 0 ? null : await backend.Health(cancel);
      var rows = new JArray(new[] { desktop, managed }.OfType<JObject>().SelectMany(it => it["solutions"]?.OfType<JObject>() ?? [])
         .DistinctBy(it => (string?)it["path"], StringComparer.OrdinalIgnoreCase));
      return new JObject { ["solutions"] = rows, ["openingWorktrees"] = new JArray(opening),
                           ["indexing"] = opening.Length > 0 || rows.Any(it => (bool?)it["indexing"] != false || it["healthFailed"] != null),
                           ["pendingDiskChanges"] = rows.Sum(it => (int?)it["pendingDiskChanges"] ?? 0) };
   }

   public async Task Remove(string path, CancellationToken cancel)
   {
      if(!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute worktree path is required.");
      path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
      var registered = worktrees.Registration(path);
      if(registered == null || !RegisteredWorktree.SamePath(registered.Root, path))
         throw new InvalidOperationException("Specify the root of a registered, trusted linked Git worktree.");
      var desktop = await ordinary.Solutions(cancel);
      if(desktop.Any(it => RegisteredWorktree.SamePath(it.WorkTree, path)))
         throw new InvalidOperationException("An ordinary Rider has this worktree open.");
      await worktrees.CloseRequested(registered, path, true, cancel);
   }

   static string? AbsoluteSolution(string? value) =>
      value != null && Path.IsPathFullyQualified(value) && (Path.GetExtension(value).ToLowerInvariant() is ".sln" or ".slnx") ? Path.GetFullPath(value) : null;
}
