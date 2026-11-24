using Compze.Threading.ResourceAccess;

namespace Royder.Primary.Worktrees;

/// <summary>
///   How far a worktree's warm-up has come: how long it has run, and what it waits for now, such as Rider indexing the
///   solution. A call that comes while it runs is told so, and the log says so when the warm-up fails.
/// </summary>
/// <remarks>
///   Until 2026-11-24 such a call was told only that the solution was still opening, which gave an agent nothing to
///   decide by whether to wait or to fall back: one was told so for 20 minutes.
/// </remarks>
sealed class WarmUp(TimeProvider clock)
{
   readonly long _started = clock.GetTimestamp();
   readonly IThreadShared<Stage> _stage = IThreadShared.New(new Stage());

   /// <summary>Records what the warm-up waits for from now on, said as what follows "waits for".</summary>
   public void WaitsFor(string what) => _stage.Locked(it => it.WaitingFor = what);

   public override string ToString() =>
      $"it has run {clock.GetElapsedTime(_started).TotalMinutes:0.#} minutes and waits for {_stage.Locked(it => it.WaitingFor)}";

   sealed class Stage
   {
      public string WaitingFor = "another worktree's open or close to finish";
   }
}
