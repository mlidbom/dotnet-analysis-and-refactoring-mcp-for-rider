using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.ProjectModel;
using Newtonsoft.Json.Linq;
using ReSharperMcp.Tools;

namespace ReSharperMcp.Experiments
{
  /// <summary>
  ///   The experiment of 2026-11-14 behind updating the tools without restarting Rider: loads a build of
  ///   <c>experiments/SwappableTool</c> into a collectible load context of its own, calls its tool, unloads the context
  ///   the previous call loaded, and reports whether the garbage collector freed it.
  /// </summary>
  /// <remarks>
  ///   Through reflection: the plugin targets net472, which has no <c>AssemblyLoadContext</c>, though Rider's backend
  ///   runs it on .NET 10.
  /// </remarks>
  public sealed class SwapToolExperiment : IMcpTool
  {
    static readonly Type LoadContextType =
      typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext", throwOnError: true);

    readonly ISolution _solution;
    object _currentContext;
    int _loads;

    public SwapToolExperiment(ISolution solution) => _solution = solution;

    public string Name => "experiment_swap_tool";
    public string Description => "Experiment: load a build of experiments/SwappableTool into a collectible load context, call it, and unload the previous one.";
    public object InputSchema => new
    {
      type = "object",
      properties = new { assemblyPath = new { type = "string", description = "The SwappableTool.dll build to load" } },
      required = new[] { "assemblyPath" }
    };

    public object Execute(JObject arguments)
    {
      var previous = UnloadTheCurrentContext();
      var answer = LoadAndCall(arguments["assemblyPath"].Value<string>());
      return new { answer, previousContextWasFreed = previous is null ? (bool?)null : WasFreed(previous) };
    }

    /// <summary>Not inlined, so that no reference to the context or what it loaded outlives the call in a local.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    object LoadAndCall(string assemblyPath)
    {
      // A copy, so the build's file stays free to be rebuilt while the context holds the loaded one.
      var copy = Path.Combine(Path.GetTempPath(), $"SwappableTool.{Guid.NewGuid():N}.dll");
      File.Copy(assemblyPath, copy);

      _currentContext = Activator.CreateInstance(LoadContextType, $"ReSharperMcp.SwappableTool.{++_loads}", true);
      var assembly = (Assembly)LoadContextType.GetMethod("LoadFromAssemblyPath").Invoke(_currentContext, new object[] { copy });
      var tool = (IMcpTool)Activator.CreateInstance(assembly.GetType("SwappableTool.ProjectCountingTool", throwOnError: true), _solution);
      return tool.Execute(new JObject());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    WeakReference UnloadTheCurrentContext()
    {
      if (_currentContext is null) return null;
      var weak = new WeakReference(_currentContext);
      LoadContextType.GetMethod("Unload").Invoke(_currentContext, null);
      _currentContext = null;
      return weak;
    }

    static bool WasFreed(WeakReference context)
    {
      for (var attempt = 0; attempt < 10 && context.IsAlive; attempt++)
      {
        GC.Collect();
        GC.WaitForPendingFinalizers();
      }
      return !context.IsAlive;
    }
  }
}
