using System;

namespace DotNetAnalysisAndRefactoringMcp.Threading;

/// <summary>The <see cref="ICriticalSection"/> that every <see cref="IThreadShared{TShared}"/> has.</summary>
/// <remarks>
///   rationale:violation-approved:Magnus-2026-11-15: the plugin's one raw lock. The rule on locking says to use
///   Compze.Threading, which the plugin can't reference.
/// </remarks>
sealed class CriticalSection : ICriticalSection
{
   readonly object _lock = new();

   public TReturn Locked<TReturn>(Func<TReturn> func)
   {
      lock(_lock) return func();
   }

   public void Locked(Action action)
   {
      lock(_lock) action();
   }
}
