using Compze.Threading.ResourceAccess;

namespace Royder.Primary.Worktrees;

/// <summary>One incarnation of a hosted worktree. Its calls hold leases until Rider has finished answering.</summary>
sealed class ManagedWorktree(RegisteredWorktree registration, string solutionPath, TimeProvider clock)
{
   readonly IThreadShared<Activity> _activity = IThreadShared.New(new Activity(clock.GetTimestamp()));
   public RegisteredWorktree Registration { get; } = registration;
   public string SolutionPath { get; } = solutionPath;

   /// <summary>
   ///   Whether the solution file was written since Rider had it open and ready. Rider's model of the solution can't be
   ///   trusted then, and the project is reopened.
   /// </summary>
   /// <remarks>
   ///   On 2026-11-24 the private Rider took in a project added to a worktree's <c>.slnx</c> by reloading the solution
   ///   with the projects the edit touched only, and dropped the other 28: their files were "in no project", and
   ///   <c>reload_projects</c> couldn't find them. In <c>verify_on_demand.py --check-solution-edit</c> it never took in
   ///   the new project. A desktop Rider took in the same kind of edit as it should.
   /// </remarks>
   /// <remarks>
   ///   The time is taken once the open is ready rather than as it begins, in case Rider writes the file as it opens it:
   ///   the project would be reopened again and again.
   /// </remarks>
   public bool SolutionFileChanged
   {
      get
      {
         var written = SolutionFileWritten();
         return _activity.Locked(it => it.Ready && written != null && written != it.SolutionFileWrittenWhenReady);
      }
   }

   /// <summary>
   ///   Whether Rider's caches of the solution are corrupted, as its health said (<c>cachesCorrupted</c>): Rider stores
   ///   nothing it learns of the code any more, and its answers go out of date. Closing the project deletes the caches,
   ///   and the project is opened again with new ones.
   /// </summary>
   /// <remarks>
   ///   On 2026-11-28 the caches of two ClaudeTooling worktrees failed within three minutes of opening. In one of them
   ///   <c>inspect_code</c> answered errors from a moment when a file was half written for half an hour, while the build
   ///   and the live daemon found none: solution-wide analysis no longer re-analyzed the files that use a declaration
   ///   that changed.
   /// </remarks>
   public bool CachesCorrupted => _activity.Locked(it => it.CachesCorrupted);

   public void MarkCachesCorrupted() => _activity.Locked(it => it.CachesCorrupted = true);

   /// <summary>Whether Rider's model of the solution can't be trusted any more, and the project is to be opened again.</summary>
   public bool NeedsReopening => SolutionFileChanged || CachesCorrupted;

   public bool Ready => _activity.Locked(it => it.Ready);
   public TimeSpan IdleFor => _activity.Locked(it => clock.GetElapsedTime(it.LastUse));
   public bool Idle => _activity.Locked(it => it.CanClose(clock.GetUtcNow()));

   public void MarkReady()
   {
      var written = SolutionFileWritten();
      _activity.Locked(it =>
      {
         it.Ready = true;
         it.LastUse = clock.GetTimestamp();
         it.SolutionFileWrittenWhenReady = written;
      });
   }

   DateTime? SolutionFileWritten() => File.Exists(SolutionPath) ? File.GetLastWriteTimeUtc(SolutionPath) : null;

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
      public bool CachesCorrupted;
      public DateTime? SolutionFileWrittenWhenReady;
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
