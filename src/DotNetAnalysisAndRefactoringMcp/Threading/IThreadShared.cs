using System;

namespace DotNetAnalysisAndRefactoringMcp.Threading;

/// <summary>
///   An object shared between threads, reached only inside its lock: the names and shape of Compze.Threading's
///   <c>IThreadShared</c>, without its timeouts and monitors, which nothing here needs.
/// </summary>
/// <remarks>
///   todo: replace with Compze.Threading once the plugin can reference Compze. That takes Rider's backend on a .NET
///   that Compze targets (Rider 2026.3 runs .NET 10, Compze .NET 11), a plugin SDK that builds plugins for it (both SDKs
///   we use build for net472 only), and the same of every Rider the plugin still supports. With the same names, the
///   call sites stay as they are.
/// </remarks>
public interface IThreadShared<out TShared>
{
   /// <summary>The critical section that protects the shared object.</summary>
   ICriticalSection CriticalSection { get; }

   /// <summary>Takes the lock, passes the shared object to <paramref name="func"/>, and returns its result.</summary>
   TResult Locked<TResult>(Func<TShared, TResult> func);

   /// <summary>Takes the lock and passes the shared object to <paramref name="action"/>.</summary>
   void Locked(Action<TShared> action);
}

/// <summary>Creates <see cref="IThreadShared{TShared}"/>s, as Compze's <c>IThreadShared.New</c> does.</summary>
/// <remarks>
///   A static class where Compze has an interface with a static member: Rider marks static interface members an error
///   for a net472 plugin, a C# 15 feature, so the call sites read the same with no such member (2026-11-15).
/// </remarks>
// ReSharper disable once InconsistentNaming
public static class IThreadShared
{
   //review: named like an interface, against the naming rule, so the call sites read as Compze's IThreadShared.New does
   //   and stay as they are when Compze.Threading replaces this. ThreadShared.New would satisfy the rule instead.

   /// <summary>A new <see cref="IThreadShared{TShared}"/> that protects <paramref name="shared"/> with a critical section of its own.</summary>
   public static IThreadShared<TShared> New<TShared>(TShared shared) => new ThreadShared<TShared>(shared, new CriticalSection());

   sealed class ThreadShared<TShared>(TShared shared, ICriticalSection criticalSection) : IThreadShared<TShared>
   {
      readonly TShared _shared = shared;

      public ICriticalSection CriticalSection { get; } = criticalSection;

      public TResult Locked<TResult>(Func<TShared, TResult> func) => CriticalSection.Locked(() => func(_shared));

      public void Locked(Action<TShared> action) => CriticalSection.Locked(() => action(_shared));
   }
}
