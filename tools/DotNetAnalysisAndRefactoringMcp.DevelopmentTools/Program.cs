using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Plugin;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Riders;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentTools;

/// <summary>
///   The command line of the plugin's development tools: the <see cref="SandboxRider"/> that builds are tested in, and
///   deploying a build to the <see cref="EverydayRider"/>s.
/// </summary>
static class Program
{
   const string Usage = """
                        Usage: dotnet run --project tools/DotNetAnalysisAndRefactoringMcp.DevelopmentTools -- <command>

                          sandbox start [<solution>]  Starts the sandbox Rider, opens the solution, waits until its MCP server
                                                      answers on port 23841 with the solution indexed
                          sandbox stop                Closes the sandbox Rider
                          sandbox place               Puts the sandbox's windows in their place again
                          sandbox status              Whether it runs, where its windows are, what its MCP server answers
                          sandbox install             Builds the working tree and puts it in the sandbox: only the tools,
                                                      served from the next call, unless the host changed; then the plugin,
                                                      restarting the sandbox if it runs
                          everyday deploy             Builds the working tree and deploys it to the everyday Riders: the
                                                      tools, which every one of them serves from its next call, and the
                                                      plugin if the host changed, which takes effect when they restart
                          everyday rollback           Removes the newest tools build, so the everyday Riders go back to the
                                                      one before it at their next call
                          everyday rollback-plugin    Puts back the plugin build the last deploy that changed the host
                                                      replaced; it takes effect when the everyday Riders restart
                        """;

   static int Main(string[] arguments)
   {
      var rider = RiderInstallation.OfThisUser();
      var sandbox = new SandboxRider(rider);
      var everyday = new EverydayRider(rider);

      switch(arguments)
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
            foreach(var line in sandbox.Status()) Console.WriteLine(line);
            break;
         case ["sandbox", "install"]:
            Console.WriteLine(sandbox.Install(PluginBuild.FromTheWorkingTree(rider))
                                 ? "Installed the plugin in the sandbox: the host changed."
                                 : "Deployed the tools to the sandbox: it serves them from its next call.");
            break;
         case ["everyday", "deploy"]:
            Deploy(everyday, sandbox, PluginBuild.FromTheWorkingTree(rider));
            break;
         case ["everyday", "rollback"]:
            Console.WriteLine($"Removed the tools build {everyday.Tools.RemoveTheNewest()}: the everyday Riders serve the one before it from their next call.");
            break;
         case ["everyday", "rollback-plugin"]:
            everyday.Plugin.PutBackTheBuildItReplaced();
            Console.WriteLine("Put back the replaced plugin build in the everyday Riders. It takes effect when they restart.");
            break;
         default:
            Console.Error.WriteLine(Usage);
            return 1;
      }

      return 0;
   }

   /// <remarks>
   ///   The tools are deployed even when the host changed: running Riders keep serving the newest build for the host
   ///   they run, and after the restart they serve this one.
   /// </remarks>
   static void Deploy(EverydayRider everyday, SandboxRider sandbox, PluginBuild build)
   {
      var hostChanged = !everyday.Plugin.HasTheHostOf(build);
      if(hostChanged) everyday.Plugin.Install(build);
      var name = everyday.Tools.Deploy(build);
      var waiting = everyday.RunningTheHostBeforeTheInstalledOne(sandbox.ProcessId);
      Console.WriteLine(hostChanged
                           ? $"The host changed: installed the plugin, and deployed the tools build {name}. Both take effect when the everyday Riders restart."
                           : waiting > 0
                              ? $"Deployed the tools build {name}, for the host installed at {everyday.Plugin.HostInstalledAt!.Value.ToLocalTime():HH:mm}: " +
                                (waiting == 1
                                    ? "an everyday Rider started before then still runs the host before it, and serves this build after its restart."
                                    : $"{waiting} everyday Riders started before then still run the host before it, and serve this build after their restart.")
                              : $"Deployed the tools build {name}: every everyday Rider serves it from its next call, with no restart.");
   }
}
