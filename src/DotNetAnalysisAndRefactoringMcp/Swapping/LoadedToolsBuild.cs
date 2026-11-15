using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DotNetAnalysisAndRefactoringMcp.Threading;
using DotNetAnalysisAndRefactoringMcp.Tools;
using JetBrains.ProjectModel;

namespace DotNetAnalysisAndRefactoringMcp.Swapping;

/// <summary>
///   A tools build loaded into a collectible load context of its own. It counts the calls it serves, and once it's
///   retired, which a newer build does, it unloads as soon as the last of them is done.
/// </summary>
/// <remarks>
///   The calls in progress finish on the build they started on, and new calls go to the newer build at once: nothing
///   waits for anything.
/// </remarks>
sealed class LoadedToolsBuild
{
   readonly IThreadShared<Calls> _calls = IThreadShared.New(new Calls());
   CollectibleLoadContext _context;
   IMcpToolsEntryPoint _entryPoint;

   LoadedToolsBuild(string folder, CollectibleLoadContext context, IMcpToolsEntryPoint entryPoint)
   {
      Folder = folder;
      _context = context;
      _entryPoint = entryPoint;
   }

   /// <summary>The build's folder.</summary>
   public string Folder { get; }

   /// <summary>The build's folder's name, which tells it from other builds.</summary>
   public string Name => Path.GetFileName(Folder);

   /// <remarks>Not inlined, so that no reference to what it loaded outlives the call in a local of its caller.</remarks>
   [MethodImpl(MethodImplOptions.NoInlining)]
   public static LoadedToolsBuild Load(string folder)
   {
      var context = new CollectibleLoadContext($"DotNetAnalysisAndRefactoringMcp.Tools {Path.GetFileName(folder)}");
      try
      {
         var assembly = context.Load(Path.Combine(folder, McpToolsEntryPoint.AssemblyFileName));
         var entryPoint = (IMcpToolsEntryPoint)Activator.CreateInstance(assembly.GetType(McpToolsEntryPoint.TypeName, throwOnError: true));
         return new LoadedToolsBuild(folder, context, entryPoint);
      }
      catch
      {
         context.Unload();
         throw;
      }
   }

   /// <summary>Counts a call in, unless the build is retired.</summary>
   public bool TryEnter()
   {
      return _calls.Locked(it =>
      {
         if(it.Retired) return false;
         it.InProgress++;
         return true;
      });
   }

   /// <summary>Counts in one more call while one is counted in already, retired or not: the build can't have unloaded.</summary>
   public void EnterAgain()
   {
      _calls.Locked(it => it.InProgress++);
   }

   /// <summary>The tools for one call, which must be counted in.</summary>
   public IReadOnlyList<IMcpTool> CreateTools(ISolution solution) => _entryPoint.CreateTools(solution);

   /// <summary>The usage guide, for a call that is counted in. A string, which keeps nothing of the build loaded.</summary>
   public string Instructions => _entryPoint.Instructions;

   /// <summary>Counts a call out, and unloads the build if it's retired and that was its last call.</summary>
   public void Exit()
   {
      if(_calls.Locked(it => --it.InProgress == 0 && it.Retired)) Unload();
   }

   /// <summary>
   ///   Takes no more calls, and unloads once the calls in progress are done, then hands
   ///   <paramref name="whenUnloaded"/> a weak reference to the load context, which clears once it's freed.
   /// </summary>
   public void Retire(Action<WeakReference> whenUnloaded)
   {
      var unload = _calls.Locked(it =>
      {
         it.Retired = true;
         it.WhenUnloaded = whenUnloaded;
         return it.InProgress == 0;
      });

      if(unload) Unload();
   }

   void Unload()
   {
      _entryPoint = null;
      var context = _context.Unload();
      _context = null;
      _calls.Locked(it => it.WhenUnloaded)(context);
   }

   /// <summary>The calls the build serves, and whether it's retired: what the threads that serve calls share.</summary>
   sealed class Calls
   {
      public int InProgress;
      public bool Retired;
      public Action<WeakReference> WhenUnloaded;
   }
}

/// <summary>One call counted in on a build, and counted out when disposed.</summary>
public sealed class ToolsLease : IDisposable
{
   LoadedToolsBuild _build;

   internal ToolsLease(LoadedToolsBuild build) => _build = build;

   public IReadOnlyList<IMcpTool> CreateTools(ISolution solution) => _build.CreateTools(solution);

   public string Instructions => _build.Instructions;

   /// <summary>A second lease on the same build, for work that may outlive this one, such as a call that timed out.</summary>
   public ToolsLease Extend()
   {
      _build.EnterAgain();
      return new ToolsLease(_build);
   }

   public void Dispose()
   {
      _build?.Exit();
      _build = null;
   }
}
