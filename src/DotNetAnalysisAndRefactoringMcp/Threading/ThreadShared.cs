using System;

namespace DotNetAnalysisAndRefactoringMcp.Threading;

/// <summary>Creates <see cref="IThreadShared{TShared}"/>s.</summary>
public static class ThreadShared
{
   /// <summary>A new <see cref="IThreadShared{TShared}"/> that protects <paramref name="shared"/> with a critical section of its own.</summary>
   public static IThreadShared<TShared> New<TShared>(TShared shared) => new ThreadShared<TShared>(shared);
}

/// <summary>The <see cref="IThreadShared{TShared}"/> that <see cref="ThreadShared.New{TShared}"/> creates.</summary>
sealed class ThreadShared<TShared>(TShared shared) : IThreadShared<TShared>
{
   readonly TShared _shared = shared;
   readonly ICriticalSection _criticalSection = new CriticalSection();

   public TResult Locked<TResult>(Func<TShared, TResult> func) => _criticalSection.Locked(() => func(_shared));

   public void Locked(Action<TShared> action) => _criticalSection.Locked(() => action(_shared));
}
