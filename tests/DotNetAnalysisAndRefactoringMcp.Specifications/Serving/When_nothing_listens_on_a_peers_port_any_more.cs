using System.Net;
using System.Net.Sockets;
using Compze.Must;
using Compze.xUnitBDD;

// ReSharper disable InconsistentNaming

namespace DotNetAnalysisAndRefactoringMcp.Specifications.Serving;

/// <summary>
///   A peer's Rider that crashed or was killed never deregistered its solutions. Until 2026-11-24 they stayed listed
///   until a call to one of them was refused, and a call that named no solution found several open.
/// </summary>
public class When_nothing_listens_on_a_peers_port_any_more
{
   readonly string _refusal;
   readonly string[] _stillListedOnThePort;

   public When_nothing_listens_on_a_peers_port_any_more(TheSandboxWithTheFixtureSolution sandbox)
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

   [XF] public void a_call_naming_the_peers_solution_is_refused_as_not_open() =>
      _refusal.Must().Contain("not found");

   [XF] public void no_solution_registered_on_the_refused_port_remains_listed() =>
      _stillListedOnThePort.Must().HaveCount(0);
}
