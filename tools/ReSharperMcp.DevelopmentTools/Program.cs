using ReSharperMcp.DevelopmentEnvironment.Plugin;
using ReSharperMcp.DevelopmentEnvironment.Riders;

namespace ReSharperMcp.DevelopmentTools;

/// <summary>
///   The command line of the plugin's development tools: the <see cref="SandboxRider"/> that builds are tested in, and
///   deploying a build to the <see cref="EverydayRider"/>.
/// </summary>
static class Program
{
  const string Usage = """
                       Usage: dotnet run --project tools/ReSharperMcp.DevelopmentTools -- <command>

                         sandbox start [<solution>]  Starts the sandbox Rider, opens the solution, waits until its MCP server
                                                     answers on port 23841 with the solution indexed
                         sandbox stop                Closes the sandbox Rider
                         sandbox place               Puts the sandbox's windows in their place again
                         sandbox status              Whether it runs, where its windows are, what its MCP server answers
                         sandbox install             Builds the working tree and installs it in the sandbox, restarting it
                                                     if it runs
                         everyday deploy             Builds the working tree and installs it in the everyday Rider; it takes
                                                     effect when that Rider restarts
                         everyday rollback           Puts back the build the last deploy replaced
                       """;

  static int Main(string[] arguments)
  {
    var rider = RiderInstallation.OfThisUser();
    var sandbox = new SandboxRider(rider);

    switch (arguments)
    {
      case ["sandbox", "start"]:
        sandbox.Start();
        break;
      case ["sandbox", "start", var solution]:
        sandbox.Start(Path.GetFullPath(solution));
        break;
      case ["sandbox", "stop"]:
        sandbox.Stop();
        break;
      case ["sandbox", "place"]:
        sandbox.PlaceWindows();
        break;
      case ["sandbox", "status"]:
        foreach (var line in sandbox.Status()) Console.WriteLine(line);
        break;
      case ["sandbox", "install"]:
        InstallInTheSandbox(sandbox, rider);
        break;
      case ["everyday", "deploy"]:
        new EverydayRider(rider).Plugin.Install(PluginBuild.FromTheWorkingTree(rider));
        Console.WriteLine("Installed in the everyday Rider. It takes effect when that Rider restarts.");
        break;
      case ["everyday", "rollback"]:
        new EverydayRider(rider).Plugin.PutBackTheBuildItReplaced();
        Console.WriteLine("Put back the replaced build in the everyday Rider. It takes effect when that Rider restarts.");
        break;
      default:
        Console.Error.WriteLine(Usage);
        return 1;
    }
    return 0;
  }

  static void InstallInTheSandbox(SandboxRider sandbox, RiderInstallation rider)
  {
    var build = PluginBuild.FromTheWorkingTree(rider);
    var wasRunning = sandbox.IsRunning;
    sandbox.Stop();
    sandbox.Plugin.Install(build);
    if (wasRunning) sandbox.Start();
  }
}
