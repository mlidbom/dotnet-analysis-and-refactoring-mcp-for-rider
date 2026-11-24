using System;
using System.ComponentModel;
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
      catch(Win32Exception denied) when(denied.NativeErrorCode == AccessDenied) { return false; } // Not ours: see AccessDenied.
   }

   /// <summary><c>ERROR_ACCESS_DENIED</c>: the process can't be opened to read its start time.</summary>
   /// <remarks>
   ///   A process the private Rider started runs as this user and can be opened, so one that can't is another that has
   ///   the PID since. On 2026-11-24 such a reused PID threw from here: the primary's maintenance logged "Access is
   ///   denied" all day, and after the private Rider's processes were ended, the primary failed to start until the
   ///   record of their run was moved aside.
   /// </remarks>
   const int AccessDenied = 5;

   public bool SameProcess(RiderProcessIdentity other) => Pid == other.Pid && Started == other.Started;
}
