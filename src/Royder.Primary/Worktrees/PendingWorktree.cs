namespace Royder.Primary.Worktrees;

/// <summary>A shared warm-up. Cancelling an individual tool call stops its wait; closing its project cancels the warm-up.</summary>
sealed class PendingWorktree
{
   readonly CancellationTokenSource _closed = new();
   readonly Lazy<Task<ManagedWorktree>> _ready;

   public string Solution { get; }

   /// <summary>How far it has come, for a call that comes before it is done.</summary>
   public WarmUp WarmUp { get; }

   public PendingWorktree(string solution, TimeProvider clock, Func<WarmUp, CancellationToken, Task<ManagedWorktree>> start)
   {
      Solution = solution;
      WarmUp = new WarmUp(clock);
      _ready = new(() => start(WarmUp, _closed.Token));
   }

   public Task<ManagedWorktree> Ready => _ready.Value;
   public void Cancel() => _closed.Cancel();
}
