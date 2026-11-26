using System.Collections.Concurrent;
using Royder.Primary.Hosting;
using Royder.Primary.Transport;

namespace Royder.Primary.Worktrees;

/// <summary>Owns the worktrees in one private Rider JVM, from opening a project to releasing the last worker.</summary>
sealed class ManagedWorktrees(PrimarySettings settings, SharedRiderHost host, RiderReadiness readiness, OrdinaryRiders ordinary,
                             WorktreeJournal journal, TimeProvider clock, IHostApplicationLifetime lifetime,
                             ILogger<ManagedWorktrees> log) : BackgroundService
{
   readonly ConcurrentDictionary<string, ManagedWorktree> _open = new(StringComparer.OrdinalIgnoreCase);
   readonly ConcurrentDictionary<RegisteredWorktree, PendingWorktree> _starting = new();
   readonly SemaphoreSlim _changes = new(1);
   public ManagedWorktree[] Open => [.._open.Values.Where(it => it.Ready)];
   public string[] Opening => [.._starting.Keys.Select(it => it.Root)];

   public RegisteredWorktree? Registration(string path) => RegisteredWorktree.Find(path, settings.Trusted);

   public override async Task StartAsync(CancellationToken cancel)
   {
      await journal.Recover(host, settings, cancel);
      await base.StartAsync(cancel);
   }

   /// <summary>How long Rider may take to open a solution, once no other worktree's open or close is in the way.</summary>
   static readonly TimeSpan HowLongOpeningMayTake = TimeSpan.FromMinutes(4);

   /// <summary>
   ///   How long Rider may take, once it has a solution open, to load its projects, index it and run its source
   ///   generators, before the warm-up fails and its project is closed.
   /// </summary>
   /// <remarks>
   ///   Until 2026-11-24 the four minutes of <see cref="HowLongOpeningMayTake"/> covered this too, counted from the call
   ///   that started the warm-up, the time queued behind other worktrees included. A build in a Compze worktree made the
   ///   private Rider reload all 117 of its projects in the middle of the warm-up, and its source generators waited for
   ///   that: the warm-up failed twice, each next call started over from a cold open, and an agent was told "still
   ///   opening" for 20 minutes. The open that the build left alone was ready in 2 minutes.
   /// </remarks>
   static readonly TimeSpan HowLongWarmingUpMayTake = TimeSpan.FromMinutes(15);

   public async Task<ManagedWorktree.Lease> Use(RegisteredWorktree registration, string solution, AnswerDeadline deadline, CancellationToken wait)
   {
      if(_open.TryGetValue(registration.Root, out var existing) && existing.Registration == registration &&
         RegisteredWorktree.SamePath(existing.SolutionPath, solution) && !existing.SolutionFileChanged && existing.TryUse() is { } lease) return lease;
      var start = _starting.GetOrAdd(registration, _ => new PendingWorktree(solution, clock, (warmUp, cancel) => Start(registration, solution, warmUp, cancel)));
      var ready = start.Ready;
      ManagedWorktree opened;
      try { opened = await deadline.Awaited(ready, wait); }
      catch(TimeoutException) when(!ready.IsCompleted)
      {
         throw new InvalidOperationException($"'{start.Solution}' is still opening, which takes about a minute from a cold start: {start.WarmUp}. Only the warm-up continues: retry the tool call in half a minute.");
      }
      if(!RegisteredWorktree.SamePath(opened.SolutionPath, solution)) throw new InvalidOperationException($"This worktree already serves '{opened.SolutionPath}'.");
      if(Registration(registration.Root) != registration) throw new InvalidOperationException("The worktree changed during startup.");
      return opened.TryUse() ?? throw new InvalidOperationException("This worktree is closing.");
   }

   async Task<ManagedWorktree> Start(RegisteredWorktree registration, string solution, WarmUp warmUp, CancellationToken stop)
   {
      using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stop, lifetime.ApplicationStopping);
      try
      {
         return await OpenProject(registration, solution, warmUp, stopping.Token);
      }
      catch(Exception ex) { log.LogWarning("Opening {Solution}, {WarmUp}: {Reason}", solution, warmUp, ex.Message); throw; }
      finally { _starting.TryRemove(registration, out _); }
   }

   async Task<ManagedWorktree> OpenProject(RegisteredWorktree registration, string solution, WarmUp warmUp, CancellationToken cancel)
   {
      ManagedWorktree? opened = null;
      try
      {
         ManagedWorktree project;
         await _changes.WaitAsync(cancel);
         try
         {
            if(Registration(registration.Root) != registration) throw new InvalidOperationException("The requested Git registration changed.");
            _open.TryGetValue(registration.Root, out var existing);
            if(existing != null && existing.Registration == registration && existing.Ready && !existing.SolutionFileChanged) return existing;
            warmUp.WaitsFor("Rider to open the solution");
            project = await Within(HowLongOpeningMayTake, "Opening the solution in Rider", cancel, async opening =>
            {
               if(existing != null) await Close(existing, existing.Registration != registration, opening);
               await RequireNoOrdinaryRider(registration, opening);
               opening.ThrowIfCancellationRequested();
               opened = new ManagedWorktree(registration, solution, clock);
               journal.Opening(registration, solution);
               _open[registration.Root] = opened;
               await host.OpenAsync(registration, solution, opening);
               return opened;
            });
         }
         finally { _changes.Release(); }

         return await Within(HowLongWarmingUpMayTake, "Loading, indexing and generating the solution's sources in Rider", cancel, async warming =>
         {
            await readiness.Wait(project, warmUp, warming);
            await _changes.WaitAsync(warming);
            try
            {
               if(!_open.TryGetValue(registration.Root, out var current) || current != project || Registration(registration.Root) != registration)
                  throw new InvalidOperationException("The worktree changed during startup.");
               await RequireNoOrdinaryRider(registration, warming);
               project.MarkReady();
               return project;
            }
            finally { _changes.Release(); }
         });
      }
      catch(Exception openingFailure)
      {
         if(opened != null)
         {
            await _changes.WaitAsync();
            try
            {
               if(_open.TryGetValue(registration.Root, out var same) && same == opened)
                  await Close(opened, Registration(registration.Root) != registration, CancellationToken.None);
            }
            catch(Exception closeFailure) { throw new AggregateException("The project failed to open and cleanup also failed.", openingFailure, closeFailure); }
            finally { _changes.Release(); }
         }
         throw;
      }
   }

   /// <summary>
   ///   The result of <paramref name="step"/>, a step of a warm-up, which is given up with a <see cref="TimeoutException"/>
   ///   naming it, <paramref name="what"/>, once it has taken <paramref name="limit"/>.
   /// </summary>
   static async Task<T> Within<T>(TimeSpan limit, string what, CancellationToken cancel, Func<CancellationToken, Task<T>> step)
   {
      using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel);
      deadline.CancelAfter(limit);
      try { return await step(deadline.Token); }
      catch(OperationCanceledException) when(deadline.IsCancellationRequested && !cancel.IsCancellationRequested)
      {
         throw new TimeoutException($"{what} took longer than {limit.TotalMinutes} minutes.");
      }
   }

   async Task RequireNoOrdinaryRider(RegisteredWorktree tree, CancellationToken cancel)
   {
      if((await ordinary.Solutions(cancel)).Any(it => RegisteredWorktree.SamePath(it.WorkTree, tree.Root)))
         throw new InvalidOperationException($"An ordinary Rider has worktree '{tree.Root}' open.");
   }

   /// <summary>
   ///   Closes the project open for <paramref name="solutionPath"/> in a worktree Git no longer lists, such as one
   ///   <c>git worktree remove</c> unregistered while Rider's backend held its folder. Whether one was open.
   /// </summary>
   /// <remarks>
   ///   The maintenance loop closes such a project by itself once it is idle, and a call that comes while it does waits
   ///   for it here. Until 2026-11-24 <c>close_worktree</c> refused the solution, as in no trusted checkout, and the hub's
   ///   cleanup of the folder had nothing to ask Royder.
   /// </remarks>
   public async Task<bool> CloseUnregistered(string solutionPath, CancellationToken cancel)
   {
      foreach(var starting in _starting.Where(it => RegisteredWorktree.SamePath(it.Value.Solution, solutionPath)))
         starting.Value.Cancel();
      await _changes.WaitAsync(cancel);
      try
      {
         var opened = _open.Values.FirstOrDefault(it => RegisteredWorktree.SamePath(it.SolutionPath, solutionPath));
         if(opened == null) return false;
         await Close(opened, true, cancel);
         return true;
      }
      finally { _changes.Release(); }
   }

   /// <summary>
   ///   Closes the project open for the worktree <paramref name="expected"/>, as <c>close_worktree</c> asks, and removes the
   ///   worktree when <paramref name="remove"/>. Whether a project was open. A close returns once Rider's backend for it and
   ///   the processes that backend started have exited.
   /// </summary>
   public async Task<bool> CloseRequested(RegisteredWorktree expected, string path, bool remove, CancellationToken cancel)
   {
      if(remove) expected.RequireLinkedForRemoval();
      if(_starting.TryGetValue(expected, out var starting))
      {
         if(!remove && !RegisteredWorktree.SamePath(starting.Solution, path))
            throw new InvalidOperationException($"The worktree is opening '{starting.Solution}'.");
         starting.Cancel();
      }
      await _changes.WaitAsync(cancel);
      try
      {
         var current = Registration(path);
         if(current == null || current != expected) throw new InvalidOperationException("The Git worktree changed before it could be closed.");
         if(remove) await RequireNoOrdinaryRider(current, cancel);
         if(!remove && _open.TryGetValue(current.Root, out var selected) && !RegisteredWorktree.SamePath(path, selected.SolutionPath))
            throw new InvalidOperationException($"The worktree serves '{selected.SolutionPath}'.");
         if(_open.TryGetValue(current.Root, out var opened)) await Close(opened, opened.Registration != current, cancel);
         if(remove) await current.Remove(settings.Trusted);
         return opened != null;
      }
      finally { _changes.Release(); }
   }

   public async Task YieldToOrdinaryRider(string root, CancellationToken cancel)
   {
      if(!_open.ContainsKey(root) && !_starting.Keys.Any(it => RegisteredWorktree.SamePath(it.Root, root))) return;
      await _changes.WaitAsync(cancel);
      try
      {
         if(_open.TryGetValue(root, out var same)) await Close(same, Registration(root) != same.Registration, cancel);
      }
      finally { _changes.Release(); }
   }

   async Task Close(ManagedWorktree worktree, bool discard, CancellationToken cancel)
   {
      if(!worktree.TryBeginClose()) throw new InvalidOperationException($"'{worktree.SolutionPath}' has active calls or is closing.");
      var started = clock.GetTimestamp();
      try
      {
         await host.CloseAsync(worktree.SolutionPath, discard, cancel);
         if(_open.Count == 1) await host.StopAsync(cancel);
         journal.Closed(worktree.Registration);
         _open.TryRemove(worktree.Registration.Root, out _);
         log.LogInformation("Closed {Solution} in {Seconds:0.0} seconds.", worktree.SolutionPath, clock.GetElapsedTime(started).TotalSeconds);
      }
      catch { worktree.RetryCloseLater(); throw; }
   }

   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
   {
      using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), clock);
      while(await timer.WaitForNextTickAsync(stoppingToken))
      {
         if(!await _changes.WaitAsync(0, stoppingToken)) continue;
         try
         {
            // Each worktree on its own: until 2026-11-25 one whose close kept failing ended the pass before the others.
            foreach(var item in _open.Values)
            {
               try
               {
                  var discard = Registration(item.Registration.Root) != item.Registration;
                  if(discard && _starting.TryGetValue(item.Registration, out var starting)) starting.Cancel();
                  if(item.Idle && (discard || !_starting.ContainsKey(item.Registration) &&
                                   (!item.Ready || item.SolutionFileChanged || item.IdleFor >= TimeSpan.FromSeconds(settings.IdleSeconds))))
                     await Close(item, discard, stoppingToken);
               }
               catch(Exception ex) when(!stoppingToken.IsCancellationRequested) { log.LogWarning("Worktree maintenance: {Reason}", ex.Message); }
            }
         }
         finally { _changes.Release(); }
      }
   }

   public override async Task StopAsync(CancellationToken cancel)
   {
      await base.StopAsync(cancel);
      await _changes.WaitAsync(cancel);
      try
      {
         while(_open.Values.Any(it => !it.Idle)) await Task.Delay(100, cancel);
         await journal.Recover(host, settings, cancel);
      }
      finally { _changes.Release(); }
   }
}
