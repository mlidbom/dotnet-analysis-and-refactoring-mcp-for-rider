namespace Royder.Primary.Worktrees;

/// <summary>A shared warm-up. Cancelling an individual tool call stops its wait; closing its project cancels the warm-up.</summary>
sealed class PendingWorktree
{
   readonly CancellationTokenSource _closed = new();
   readonly Lazy<Task<ManagedWorktree>> _ready;

   public string Solution { get; }
   public PendingWorktree(string solution, Func<CancellationToken, Task<ManagedWorktree>> start)
   {
      Solution = solution;
      _ready = new(() => start(_closed.Token));
   }

   public Task<ManagedWorktree> Ready => _ready.Value;
   public void Cancel() => _closed.Cancel();
}
