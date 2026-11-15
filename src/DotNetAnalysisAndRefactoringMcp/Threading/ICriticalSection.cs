using System;

namespace DotNetAnalysisAndRefactoringMcp.Threading;

/// <summary>
///   A mutual-exclusion critical section: the names and shape of Compze.Threading's <c>ICriticalSection</c>, without its
///   timeouts. For code that shares no state of its own; shared state is an <see cref="IThreadShared{TShared}"/>.
/// </summary>
/// <remarks>todo: replace with Compze.Threading, as <see cref="IThreadShared{TShared}"/> says.</remarks>
public interface ICriticalSection
{
   /// <summary>Takes the lock, runs <paramref name="func"/>, and returns its result.</summary>
   TReturn Locked<TReturn>(Func<TReturn> func);

   /// <summary>Takes the lock and runs <paramref name="action"/>.</summary>
   void Locked(Action action);

   /// <summary>A new critical section.</summary>
   static ICriticalSection New() => new CriticalSection();

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
}
