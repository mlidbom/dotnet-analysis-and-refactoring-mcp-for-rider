using System;
using System.IO;
using System.Reflection;

namespace DotNetAnalysisAndRefactoringMcp.Swapping;

/// <summary>
///   A collectible <c>AssemblyLoadContext</c>: assemblies loaded into it are freed once it is unloaded and nothing
///   refers to them any more.
/// </summary>
/// <remarks>
///   Through reflection: the plugin targets net472, which has no <c>AssemblyLoadContext</c>, though Rider's backend
///   runs it on .NET 10. The context doesn't override <c>Load</c>, so an assembly it is asked for that it didn't load
///   itself comes from the default context: ReSharper's, Newtonsoft.Json and the host are Rider's own copies.
/// </remarks>
sealed class CollectibleLoadContext
{
  static readonly Type LoadContextType =
    typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext", throwOnError: true);

  static readonly MethodInfo LoadFromStreamMethod = LoadContextType.GetMethod("LoadFromStream", [typeof(Stream), typeof(Stream)]);
  static readonly MethodInfo UnloadMethod = LoadContextType.GetMethod("Unload", Type.EmptyTypes);

  object _context;

  public CollectibleLoadContext(string name) => _context = Activator.CreateInstance(LoadContextType, name, true);

  /// <summary>
  ///   Loads an assembly from its bytes, with its symbols for line numbers in stack traces. From bytes, not the file:
  ///   a file loaded from stays locked until the context is freed, and old builds should be free to delete.
  /// </summary>
  public Assembly Load(string assemblyFile)
  {
    var symbolsFile = Path.ChangeExtension(assemblyFile, ".pdb");
    using var assembly = new MemoryStream(File.ReadAllBytes(assemblyFile));
    using var symbols = File.Exists(symbolsFile) ? new MemoryStream(File.ReadAllBytes(symbolsFile)) : null;
    return (Assembly)LoadFromStreamMethod.Invoke(_context, [assembly, symbols]);
  }

  /// <summary>
  ///   Starts unloading, and returns a weak reference to the context, which the garbage collector clears once the
  ///   context and what it loaded have been freed.
  /// </summary>
  public WeakReference Unload()
  {
    var context = _context;
    _context = null;
    UnloadMethod.Invoke(context, null);
    return new WeakReference(context);
  }
}
