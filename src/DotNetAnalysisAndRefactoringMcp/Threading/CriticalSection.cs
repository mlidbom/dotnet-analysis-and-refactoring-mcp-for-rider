using System;

namespace DotNetAnalysisAndRefactoringMcp.Threading;

/// <summary>The <see cref="ICriticalSection"/> that every <see cref="IThreadShared{TShared}"/> has.</summary>
/// <remarks>
///   The plugin's one raw lock. The rule on locking says to use Compze.Threading, which the plugin can't reference:
///   Magnus approved this in its place (2026-11-15).
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
