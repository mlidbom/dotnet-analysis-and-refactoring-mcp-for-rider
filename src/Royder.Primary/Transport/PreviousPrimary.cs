using System.Diagnostics;

namespace Royder.Primary.Transport;

/// <summary>
///   The primary that served the public port before this one and handed it over, if one did, until it has stopped. As
///   it stops it closes its projects and stops its private Rider, which holds the ports this primary's private Rider
///   would listen on until then.
/// </summary>
sealed class PreviousPrimary
{
   readonly Process? _process;

   PreviousPrimary(Process? process) => _process = process;

   /// <summary>None: this primary bound the port itself.</summary>
   public static readonly PreviousPrimary None = new(null);

   /// <summary>The primary in the process <paramref name="processId"/>, which has just handed over the port and stops only after its clients have let go of it.</summary>
   public static PreviousPrimary Of(int processId) => new(Process.GetProcessById(processId));

   public int? ProcessId => _process?.Id;

   public bool HasStopped => _process?.HasExited ?? true;

   public Task Stopped(CancellationToken cancel) => _process?.WaitForExitAsync(cancel) ?? Task.CompletedTask;
}
