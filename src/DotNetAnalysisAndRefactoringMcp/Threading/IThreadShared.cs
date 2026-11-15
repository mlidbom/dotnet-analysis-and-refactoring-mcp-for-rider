using System;

namespace DotNetAnalysisAndRefactoringMcp.Threading;

/// <summary>
///   An object that several threads share, reached only inside its lock: what the plugin uses wherever threads share
///   state. <see cref="ThreadShared.New{TShared}"/> creates one.
/// </summary>
/// <remarks>
///   It keeps the plugin's one raw lock, in <see cref="CriticalSection"/>, where the rule on locking would have
///   Compze.Threading, which the plugin can't reference.<br/>
///   todo: replace with Compze.Threading once the plugin can reference Compze. That takes Rider's backend on a .NET
///   that Compze targets (Rider 2026.3 runs .NET 10, Compze .NET 11), a plugin SDK that builds plugins for it (both SDKs
///   we use build for net472 only), and the same of every Rider the plugin still supports.
/// </remarks>
public interface IThreadShared<out TShared>
{
   /// <summary>Takes the lock, passes the shared object to <paramref name="func"/>, and returns its result.</summary>
   TResult Locked<TResult>(Func<TShared, TResult> func);

   /// <summary>Takes the lock and passes the shared object to <paramref name="action"/>.</summary>
   void Locked(Action<TShared> action);
}
