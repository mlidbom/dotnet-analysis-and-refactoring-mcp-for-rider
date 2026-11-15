using System;

namespace DotNetAnalysisAndRefactoringMcp.Threading;

/// <summary>
///   A mutual-exclusion critical section: code that runs on one thread at a time. Shared state is an
///   <see cref="IThreadShared{TShared}"/>, which has one of its own.
/// </summary>
/// <remarks>todo: replace with Compze.Threading, as <see cref="IThreadShared{TShared}"/> says.</remarks>
public interface ICriticalSection
{
   /// <summary>Takes the lock, runs <paramref name="func"/>, and returns its result.</summary>
   TReturn Locked<TReturn>(Func<TReturn> func);

   /// <summary>Takes the lock and runs <paramref name="action"/>.</summary>
   void Locked(Action action);
}
