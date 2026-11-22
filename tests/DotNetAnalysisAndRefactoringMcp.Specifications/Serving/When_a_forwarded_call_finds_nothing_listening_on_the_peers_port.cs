using System.Net;
using System.Net.Sockets;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

public class When_a_forwarded_call_finds_nothing_listening_on_the_peers_port
{
   readonly string _refusal;
   readonly string[] _stillListedOnThePort;

   public When_a_forwarded_call_finds_nothing_listening_on_the_peers_port(TheSandboxWithTheFixtureSolution sandbox)
   {
      var calledSolution = sandbox.PathOf("CalledPeer.sln");
      var otherSolution = sandbox.PathOf("OtherPeer.sln");
      using var unusedPort = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
      unusedPort.Bind(new IPEndPoint(IPAddress.Loopback, 0));
      var port = ((IPEndPoint)unusedPort.LocalEndPoint!).Port;
      var server = sandbox.McpServer;
      try
      {
         server.RegisterPeer(calledSolution, port);
         server.RegisterPeer(otherSolution, port);
         _refusal = server.Refusal("get_solution_structure", new { solutionName = calledSolution });
         _stillListedOnThePort = server.ListSolutions().GetProperty("solutions").EnumerateArray()
                                       .Select(it => it.GetProperty("path").GetString()!)
                                       .Where(it => it == calledSolution || it == otherSolution).ToArray();
      }
      finally
      {
         server.DeregisterPeer(calledSolution);
         server.DeregisterPeer(otherSolution);
      }
   }

   [XF] public void the_primary_reports_that_the_peers_solution_is_no_longer_available() =>
      _refusal.Must().Contain("Solution is no longer available");

   [XF] public void no_solution_registered_on_the_refused_port_remains_listed() =>
      _stillListedOnThePort.Must().HaveCount(0);
}
