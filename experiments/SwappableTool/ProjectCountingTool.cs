using System;
using System.Linq;
using System.Reflection;
using JetBrains.ProjectModel;
using Newtonsoft.Json.Linq;
using ReSharperMcp.Tools;

namespace SwappableTool
{
  /// <summary>
  ///   A tool for the swapping experiment: it uses ReSharper's project model, so that loading it in a load context of its
  ///   own shows whether ReSharper's types and the plugin's resolve to the copies Rider already has loaded.
  /// </summary>
  public sealed class ProjectCountingTool : IMcpTool
  {
    readonly ISolution _solution;

    public ProjectCountingTool(ISolution solution) => _solution = solution;

    public string Name => "experiment_count_projects";
    public string Description => "The swapping experiment's tool.";
    public object InputSchema => new { type = "object", properties = new { } };

    /// <remarks>
    ///   A <see cref="JObject"/>, not an anonymous object: Newtonsoft.Json caches a contract for every type it
    ///   serializes, and a type of this assembly in that cache would keep its load context from being freed.
    /// </remarks>
    public object Execute(JObject arguments) => new JObject
    {
      ["version"] = typeof(ProjectCountingTool).Assembly.GetName().Version.ToString(),
      ["projects"] = _solution.GetAllProjects().Count(),
      ["loadContext"] = LoadContextOf(typeof(ProjectCountingTool)),
      ["pluginsLoadContext"] = LoadContextOf(typeof(IMcpTool)),
      ["resharpersLoadContext"] = LoadContextOf(typeof(ISolution))
    };

    /// <summary>
    ///   The name of the load context <paramref name="type"/>'s assembly was loaded into. Through reflection: the
    ///   assembly targets net472, which has no <c>AssemblyLoadContext</c>, though Rider runs it on .NET.
    /// </summary>
    static string LoadContextOf(Type type)
    {
      var loadContextType = typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext", throwOnError: true);
      var context = loadContextType.GetMethod("GetLoadContext", new[] { typeof(Assembly) }).Invoke(null, new object[] { type.Assembly });
      return (string)loadContextType.GetProperty("Name").GetValue(context);
    }
  }
}
