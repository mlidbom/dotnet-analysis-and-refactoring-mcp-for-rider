using System.IO;
using System.Text;
using JetBrains.Application.BuildScript;
using JetBrains.Application.BuildScript.Application;
using JetBrains.Application.Components;
using JetBrains.Application.Environment;
using JetBrains.Application.Threading;
using JetBrains.CommandLine.InspectCode;
using JetBrains.CommandLine.InspectCode.Environment;
using JetBrains.CommandLine.InspectCode.Environment.Settings;
using JetBrains.CommandLine.InspectCode.Unattended.Settings;
using JetBrains.Lifetimes;
using JetBrains.ReSharperAutomationTools.CommandLine.Common.Application;
using JetBrains.ReSharperAutomationTools.CommandLine.Common.Console.Util;
using JetBrains.Threading;
using JetBrains.Util;
using JetBrains.Util.Logging;

namespace HeadlessHost;

static class Program
{
   [STAThread]
   static int Main(string[] args)
   {
      if(args.Length < 3)
         throw new ArgumentException("Expected solution file, cache directory and Type.Member (or - for indexing only).");

      using var lifetime = Lifetime.Define("HeadlessHost");
      var shell = StartShell(lifetime.Lifetime, args[0], args[1], args.Skip(3).Where(it => it != "--listen").ToArray());
      var locks = shell.GetComponent<IShellLocks>();
      using var openedSolution = CommandLineProductMain.Main(lifetime.Lifetime, shell.GetComponent<IThreading>(),
         shell, locks, Logger.GetLogger(typeof(Program)), shell.GetComponent<IInspectCodeSettings>(),
         shell.GetComponent<InspectCodeCommandLineArguments>(), shell.GetComponent<IProductInfo>(),
         (ref int? _) => null, out var solution, out var exitCode, isLiveSession: true);
      if(exitCode.HasValue || solution == null) return exitCode ?? 1;

      var session = new SolutionSession(solution, locks, lifetime.Lifetime);
      session.MeasureWhenIndexed();
      if(args[2] != "-" && !session.FindUsages(args[2])) return 1;
      if(args.Contains("--listen")) session.Listen();
      return 0;
   }

   /// <summary>Uses InspectCode's product packages and settings. Selecting <see cref="IInspectCodeEnvironmentZone"/>
   /// lets our process decide what to do with the composed shell.</summary>
   static IComponentContainer StartShell(Lifetime lifetime, string solutionPath, string cacheDirectory, string[] extensions)
   {
      Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
      string[] options = [Path.GetFullPath(solutionPath), "--no-build", "--caches-home=" + Path.GetFullPath(cacheDirectory), .. extensions];
      var hostInfo = HostInfo.CreateForCurrentWave("HeadlessHost");
      var host = CommandLineProgram.Prepare<IInspectCodeEnvironmentZone>(typeof(InspectCodeProgram).Assembly,
         hostInfo, isEverythingOnStdErr: true, loadAllPackages: false, options);
      if(extensions.Length > 0)
      {
         if(Environment.GetEnvironmentVariable("DOTNET_ANALYSIS_AND_REFACTORING_MCP_PORT") is not ("23910" or "23920"))
            throw new InvalidOperationException("This host loads Royder only with experimental base port 23910 or 23920.");
         host = host.CustomAdd(new CommandLinePluginsMixin<InspectCodeSettingsData>(hostInfo, options));
      }
      var timeout = TimeSpan.FromMinutes(3);
      var environment = host.End().CreateDontRunAsync(lifetime).DispatcherWaitResult(timeout);
      var products = environment.EnvironmentContainer.GetComponent<RunningProducts>();
      JetDispatcher.Run(() => products.ShellContainerComposed.Value == null, timeout, bThrowOnTimeout: true);
      return products.ShellContainerComposed.Value!;
   }
}
