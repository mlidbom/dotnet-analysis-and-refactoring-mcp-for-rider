using System;
using System.Threading.Tasks;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Psi.Caches;

namespace DotNetAnalysisAndRefactoringMcp;

/// <summary>
///   ReSharper's indexing of a solution: the updating of its caches, all of them once the solution opens, and a changed
///   file's after each change. What a tool reads while it runs may be incomplete.
/// </summary>
/// <remarks>
///   A change of a few files keeps it running for tens of milliseconds (measured 2026-11-15), and queries bring those
///   files' caches up to date themselves (<c>PsiCaches.Update</c>): only a long run, such as the first after the solution
///   opens, leaves answers incomplete.
/// </remarks>
sealed class Indexing
{
   static readonly TimeSpan BetweenChecks = TimeSpan.FromMilliseconds(50);

   readonly IPsiCachesState _caches;

   public Indexing(ISolution solution) => _caches = solution.TryGetComponent<IPsiCachesState>();

   /// <summary>Whether it runs now.</summary>
   public bool IsRunning => _caches is {} caches && !caches.IsIdle.Value;

   /// <summary>
   ///   Whether it is done within <paramref name="howLong"/>: not running at two checks in a row, so that a moment
   ///   between two updates doesn't count.
   /// </summary>
   public async Task<bool> IsDoneWithin(TimeSpan howLong, Lifetime lifetime)
   {
      var deadline = DateTime.UtcNow + howLong;
      var doneBefore = false;
      while(true)
      {
         var done = !IsRunning;
         if(done && doneBefore) return true;
         if(DateTime.UtcNow >= deadline) return false;
         doneBefore = done;
         await Task.Delay(BetweenChecks, lifetime).ConfigureAwait(false);
      }
   }
}
