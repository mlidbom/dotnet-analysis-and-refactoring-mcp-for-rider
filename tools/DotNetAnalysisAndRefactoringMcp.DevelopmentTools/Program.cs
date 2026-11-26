using System.Text.Json.Nodes;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment;
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
                          sandbox status              Whether it runs, its windows on the desktop nobody sees, what its MCP
                                                      server answers
                          sandbox install             Builds the working tree and puts it in the sandbox: only the tools,
                                                      served from the next call, unless the plugin changed (its host or its
                                                      frontend jar); then the plugin, restarting the sandbox if it runs
                          sandbox call <tool> [<json>]
                                                      Calls a tool on the sandbox's MCP server, with its arguments as a JSON
                                                      object, and prints the answer as an agent reads it, and its size
                          peer start [<solution>]     Starts the second sandbox Rider, the first one's peer, opens the
                                                      solution, waits until the first's MCP server lists it indexed
                          peer stop                   Closes the peer
                          peer status                 Whether the peer runs, its windows on the desktop nobody sees
                          everyday deploy             Builds the working tree and deploys it to the everyday Riders: the
                                                      tools, which every one of them serves from its next call, and the
                                                      plugin if it changed, which takes effect when they restart
                          everyday rollback           Removes the newest tools build, so the everyday Riders go back to the
                                                      one before it at their next call
                          everyday rollback-plugin    Puts back the plugin build that the last deploy installing the plugin
                                                      replaced; it takes effect when the everyday Riders restart

                        The commands that start, stop or install a sandbox wait for the sandbox turn, which a run of the
                        specifications, this repository's or the symbol linker's, holds while it runs.
                        """;

   static int Main(string[] arguments)
   {
      var rider = RiderInstallation.OfThisUser();
      var sandbox = new SandboxRider(rider);
      var peer = SandboxRider.ThePeer(rider);
      var everyday = new EverydayRider(rider);

      switch(arguments)
      {
         case ["sandbox", "start"]:
            InTheSandboxTurn("sandbox start", () => sandbox.Start());
            break;
         case ["sandbox", "start", var solution]:
            InTheSandboxTurn("sandbox start", () => sandbox.Start(Path.GetFullPath(solution)));
            break;
         case ["sandbox", "stop"]:
            InTheSandboxTurn("sandbox stop", sandbox.Stop);
            break;
         case ["sandbox", "status"]:
            foreach(var line in sandbox.Status()) Console.WriteLine(line);
            break;
         case ["sandbox", "install"]:
            InTheSandboxTurn("sandbox install",
                             () => Console.WriteLine(sandbox.Install(PluginBuild.FromTheWorkingTree(rider))
                                                        ? "Installed the plugin in the sandbox: its host or its frontend jar changed."
                                                        : "Deployed the tools to the sandbox: it serves them from its next call."));
            break;
         case ["sandbox", "call", var tool]:
            CallTheSandbox(tool, "{}");
            break;
         case ["sandbox", "call", var tool, var toolArguments]:
            CallTheSandbox(tool, toolArguments);
            break;
         case ["peer", "start"]:
            InTheSandboxTurn("peer start", () => peer.Start());
            break;
         case ["peer", "start", var peersSolution]:
            InTheSandboxTurn("peer start", () => peer.Start(Path.GetFullPath(peersSolution)));
            break;
         case ["peer", "stop"]:
            InTheSandboxTurn("peer stop", peer.Stop);
            break;
         case ["peer", "status"]:
            foreach(var line in peer.Status()) Console.WriteLine(line);
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

   /// <summary>
   ///   Runs <paramref name="command"/>'s change to a sandbox in the <see cref="SandboxTurn"/>: it waits for a run of the
   ///   specifications to end, instead of starting, stopping or reinstalling the sandbox under it.
   /// </summary>
   static void InTheSandboxTurn(string command, Action change)
   {
      using var turn = SandboxTurn.Take($"the development tools' {command} in {RepositoryRoot.Folder}");
      change();
   }

   /// <summary>
   ///   Calls <paramref name="tool"/> on the sandbox's MCP server, as an agent's client does, and prints its answer, and
   ///   on the error stream the answer's size: to see and measure what an agent reads, without a script of one's own.
   /// </summary>
   static void CallTheSandbox(string tool, string toolArguments)
   {
      Console.OutputEncoding = System.Text.Encoding.UTF8;
      var answer = new SandboxMcpServer(SandboxRider.McpPort).CallTool(tool, JsonNode.Parse(toolArguments)!);
      Console.WriteLine(answer);
      Console.Error.WriteLine($"--- {answer.Split('\n').Length} lines, {answer.Length} characters");
   }

   /// <remarks>
   ///   The tools are deployed even when the plugin changed: running Riders keep serving the newest build for the host
   ///   they run, and after the restart they serve this one.
   /// </remarks>
   static void Deploy(EverydayRider everyday, SandboxRider sandbox, PluginBuild build)
   {
      var pluginChanged = !everyday.Plugin.HasThePluginOf(build);
      if(pluginChanged) everyday.Plugin.Install(build);
      var name = everyday.Tools.Deploy(build);
      var waiting = everyday.RunningTheHostBeforeTheInstalledOne(sandbox.ProcessId);
      Console.WriteLine(pluginChanged
                           ? $"The plugin changed, its host or its frontend jar: installed it, and deployed the tools build {name}. Both take effect when the everyday Riders restart."
                           : waiting > 0
                              ? $"Deployed the tools build {name}, for the host installed at {everyday.Plugin.HostInstalledAt!.Value.ToLocalTime():HH:mm}: " +
                                (waiting == 1
                                    ? "an everyday Rider started before then still runs the host before it, and serves this build after its restart."
                                    : $"{waiting} everyday Riders started before then still run the host before it, and serve this build after their restart.")
                              : $"Deployed the tools build {name}: every everyday Rider serves it from its next call, with no restart.");
   }
}
