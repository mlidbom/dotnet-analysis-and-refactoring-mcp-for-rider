using System;

namespace DotNetAnalysisAndRefactoringMcp.Threading;

/// <summary>
///   A mutual-exclusion critical section: the names and shape of Compze.Threading's <c>ICriticalSection</c>, without its
///   timeouts. The one that protects an <see cref="IThreadShared{TShared}"/>, which is how shared state is reached.
/// </summary>
/// <remarks>todo: replace with Compze.Threading, as <see cref="IThreadShared{TShared}"/> says.</remarks>
public interface ICriticalSection
{
   /// <summary>Takes the lock, runs <paramref name="func"/>, and returns its result.</summary>
   TReturn Locked<TReturn>(Func<TReturn> func);

   /// <summary>Takes the lock and runs <paramref name="action"/>.</summary>
   void Locked(Action action);
}
