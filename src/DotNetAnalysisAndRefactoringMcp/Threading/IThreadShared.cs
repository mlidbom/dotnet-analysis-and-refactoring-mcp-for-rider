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
public interface IThreadShared
{
   /// <summary>A new <see cref="IThreadShared{TShared}"/> that protects <paramref name="shared"/> with a critical section of its own.</summary>
   public static IThreadShared<TShared> New<TShared>(TShared shared) => new ThreadShared<TShared>(shared, ICriticalSection.New());

   sealed class ThreadShared<TShared>(TShared shared, ICriticalSection criticalSection) : IThreadShared<TShared>
   {
      public ICriticalSection CriticalSection { get; } = criticalSection;

      public TResult Locked<TResult>(Func<TShared, TResult> func) => CriticalSection.Locked(() => func(shared));

      public void Locked(Action<TShared> action) => CriticalSection.Locked(() => action(shared));
   }
}
