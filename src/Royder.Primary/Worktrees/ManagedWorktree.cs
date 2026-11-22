using Compze.Threading.ResourceAccess;

namespace Royder.Primary.Worktrees;

/// <summary>One incarnation of a hosted worktree. Its calls hold leases until Rider has finished answering.</summary>
sealed class ManagedWorktree(RegisteredWorktree registration, string solutionPath, TimeProvider clock)
{
   readonly IThreadShared<Activity> _activity = IThreadShared.New(new Activity(clock.GetTimestamp()));
   public RegisteredWorktree Registration { get; } = registration;
   public string SolutionPath { get; } = solutionPath;
   public bool Ready => _activity.Locked(it => it.Ready);
   public TimeSpan IdleFor => _activity.Locked(it => clock.GetElapsedTime(it.LastUse));
   public bool Idle => _activity.Locked(it => it.CanClose(clock.GetUtcNow()));
   public void MarkReady() => _activity.Locked(it => { it.Ready = true; it.LastUse = clock.GetTimestamp(); });

   public bool TryBeginClose() => _activity.Locked(it =>
   {
      if(!it.CanClose(clock.GetUtcNow())) return false;
      it.Ready = false;
      it.Closing = true;
      return true;
   });

   /// <summary>A partially closed project remains unavailable until a later cleanup confirms closure.</summary>
   public void RetryCloseLater() => _activity.Locked(it => it.Closing = false);

   public Lease? TryUse() => _activity.Locked(it =>
   {
      if(!it.Ready) return null;
      it.Calls++;
      return new Lease(this);
   });

   sealed class Activity(long lastUse)
   {
      public long LastUse = lastUse;
      public int Calls;
      public bool Ready;
      public bool Closing;
      public DateTimeOffset UncertainUntil;
      public bool CanClose(DateTimeOffset now) => Calls == 0 && !Closing && now >= UncertainUntil;
   }

   public sealed class Lease(ManagedWorktree worktree) : IDisposable
   {
      /// <summary>A lost reply cannot be replayed. Give a possibly dispatched tool its full execution deadline.</summary>
      public void Invalidate(bool mayStillBeExecuting) => worktree.Invalidate(mayStillBeExecuting);
      public void Dispose() => worktree.FinishedCall();
   }

   void Invalidate(bool mayStillBeExecuting) => _activity.Locked(it =>
   {
      it.Ready = false;
      if(mayStillBeExecuting) it.UncertainUntil = clock.GetUtcNow().AddSeconds(130);
   });

   void FinishedCall() => _activity.Locked(it =>
   {
      it.LastUse = clock.GetTimestamp();
      it.Calls--;
   });
}
