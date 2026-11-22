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

   public RegisteredWorktree? Registration(string path) => RegisteredWorktree.Find(path, settings.GitDirectories);

   public override async Task StartAsync(CancellationToken cancel)
   {
      await journal.Recover(host, settings, cancel);
      await base.StartAsync(cancel);
   }

   public async Task<ManagedWorktree.Lease> Use(RegisteredWorktree registration, string solution, CancellationToken wait)
   {
      if(_open.TryGetValue(registration.Root, out var existing) && existing.Registration == registration &&
         RegisteredWorktree.SamePath(existing.SolutionPath, solution) && existing.TryUse() is { } lease) return lease;
      var start = _starting.GetOrAdd(registration, _ => new PendingWorktree(solution, cancel => Start(registration, solution, cancel)));
      var ready = start.Ready;
      ManagedWorktree opened;
      try { opened = await ready.WaitAsync(TimeSpan.FromSeconds(settings.FirstCallWaitSeconds), wait); }
      catch(TimeoutException) when(!ready.IsCompleted)
      {
         throw new InvalidOperationException($"'{solution}' is still opening or indexing. Only the warm-up continues. Retry the tool call when it is ready.");
      }
      if(!RegisteredWorktree.SamePath(opened.SolutionPath, solution)) throw new InvalidOperationException($"This worktree already serves '{opened.SolutionPath}'.");
      if(Registration(registration.Root) != registration) throw new InvalidOperationException("The worktree changed during startup.");
      return opened.TryUse() ?? throw new InvalidOperationException("This worktree is closing.");
   }

   async Task<ManagedWorktree> Start(RegisteredWorktree registration, string solution, CancellationToken stop)
   {
      using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop, lifetime.ApplicationStopping);
      deadline.CancelAfter(TimeSpan.FromMinutes(4));
      var cancel = deadline.Token;
      try
      {
         return await OpenProject(registration, solution, cancel);
      }
      catch(Exception ex) { log.LogWarning("Opening {Solution}: {Reason}", solution, ex.Message); throw; }
      finally { _starting.TryRemove(registration, out _); }
   }

   async Task<ManagedWorktree> OpenProject(RegisteredWorktree registration, string solution, CancellationToken cancel)
   {
      ManagedWorktree? opened = null;
      try
      {
         await _changes.WaitAsync(cancel);
         try
         {
            if(Registration(registration.Root) != registration) throw new InvalidOperationException("The requested Git registration changed.");
            if(_open.TryGetValue(registration.Root, out var existing))
            {
               if(existing.Registration == registration && existing.Ready) return existing;
               await Close(existing, existing.Registration != registration, cancel);
            }
            await RequireNoOrdinaryRider(registration, cancel);
            cancel.ThrowIfCancellationRequested();
            opened = new ManagedWorktree(registration, solution, clock);
            journal.Opening(registration, solution);
            _open[registration.Root] = opened;
            await host.OpenAsync(registration, solution, cancel);
         }
         finally { _changes.Release(); }

         await readiness.Wait(opened, cancel);
         await _changes.WaitAsync(cancel);
         try
         {
            if(!_open.TryGetValue(registration.Root, out var current) || current != opened || Registration(registration.Root) != registration)
               throw new InvalidOperationException("The worktree changed during startup.");
            await RequireNoOrdinaryRider(registration, cancel);
            opened.MarkReady();
            return opened;
         }
         finally { _changes.Release(); }
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

   async Task RequireNoOrdinaryRider(RegisteredWorktree tree, CancellationToken cancel)
   {
      if((await ordinary.Solutions(cancel)).Any(it => RegisteredWorktree.SamePath(it.WorkTree, tree.Root)))
         throw new InvalidOperationException($"An ordinary Rider has worktree '{tree.Root}' open.");
   }

   public async Task CloseRequested(RegisteredWorktree expected, string path, bool remove, CancellationToken cancel)
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
         if(remove) await current.Remove(settings.GitDirectories);
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
      try
      {
         await host.CloseAsync(worktree.SolutionPath, discard, cancel);
         if(_open.Count == 1) await host.StopAsync(cancel);
         journal.Closed(worktree.Registration);
         _open.TryRemove(worktree.Registration.Root, out _);
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
            foreach(var item in _open.Values)
            {
               var discard = Registration(item.Registration.Root) != item.Registration;
               if(discard && _starting.TryGetValue(item.Registration, out var starting)) starting.Cancel();
               if(item.Idle && (discard || !_starting.ContainsKey(item.Registration) && (!item.Ready || item.IdleFor >= TimeSpan.FromSeconds(settings.IdleSeconds))))
                  await Close(item, discard, stoppingToken);
            }
         }
         catch(Exception ex) when(!stoppingToken.IsCancellationRequested) { log.LogWarning("Worktree maintenance: {Reason}", ex.Message); }
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
