using System;
using System.Diagnostics;

namespace Royder.Primary.Hosting;

/// <summary>A member of the private Windows job. Creation time distinguishes owners of a reused PID.</summary>
internal sealed record RiderProcessIdentity(int Pid, long Started, string Name, int? ParentPid)
{
   public bool IsAlive()
   {
      try
      {
         using var process = Process.GetProcessById(Pid);
         return process.StartTime.ToUniversalTime().Ticks == Started && !process.HasExited;
      }
      catch(ArgumentException) { return false; } // The PID is no longer allocated.
      catch(InvalidOperationException) { return false; } // Exited while querying the handle.
   }

   public bool SameProcess(RiderProcessIdentity other) => Pid == other.Pid && Started == other.Started;
}
