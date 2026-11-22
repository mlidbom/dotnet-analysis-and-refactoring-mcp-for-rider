using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RemoteDevHost;

/// <summary>Owns one experiment's Windows process tree. Windows ends the tree when its supervisor exits.</summary>
public sealed class WindowsJob : IDisposable
{
   readonly SafeFileHandle job;

   public WindowsJob()
   {
      job = CreateJobObjectW(IntPtr.Zero, null);
      if(job.IsInvalid) throw new Win32Exception();
      var limits = new ExtendedLimits();
      limits.Basic.Flags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
      if(!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<ExtendedLimits>()))
      {
         var error = new Win32Exception();
         job.Dispose();
         throw error;
      }
   }

   public static long AvailablePhysicalBytes
   {
      get
      {
         var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
         if(!GlobalMemoryStatusEx(ref memory)) throw new Win32Exception();
         return (long)memory.AvailablePhysical;
      }
   }

   /// <summary>Starts the bundled Java executable suspended so its first child already belongs to this job.
   /// The child inherits the supervisor's private environment.</summary>
   public int StartJava(string java, string argumentsFile, string directory, string logPrefix)
   {
      using var input = File.Open(Path.Combine(directory, "stdin.empty"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Inheritable);
      using var output = File.Open(logPrefix + ".stdout", FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Inheritable);
      using var error = File.Open(logPrefix + ".stderr", FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Inheritable);
      var startup = new StartupInfo {
         Size = Marshal.SizeOf<StartupInfo>(), Flags = 0x100,
         Input = input.SafeFileHandle.DangerousGetHandle(), Output = output.SafeFileHandle.DangerousGetHandle(),
         Error = error.SafeFileHandle.DangerousGetHandle()
      };
      var command = new StringBuilder("\"" + java + "\" \"@" + argumentsFile + "\"");
      if(!CreateProcessW(java, command, IntPtr.Zero, IntPtr.Zero, true, 0x08000004, IntPtr.Zero, directory, ref startup, out var child))
         throw new Win32Exception();
      using var process = new SafeFileHandle(child.Process, true);
      using var thread = new SafeFileHandle(child.Thread, true);
      if(!AssignProcessToJobObject(job, process) || ResumeThread(thread) == uint.MaxValue)
      {
         var failure = new Win32Exception();
         TerminateProcess(process, 1); // Still suspended and created by this call.
         throw failure;
      }
      return child.Pid;
   }

   public int[] ProcessIds()
   {
      for(var capacity = 32; ; capacity *= 2)
      {
         var size = 8 + IntPtr.Size * capacity;
         var buffer = Marshal.AllocHGlobal(size);
         try
         {
            if(!QueryInformationJobObject(job, 3, buffer, size, IntPtr.Zero))
            {
               var error = new Win32Exception();
               if(error.NativeErrorCode == 234) continue; // ERROR_MORE_DATA
               throw error;
            }
            var count = Marshal.ReadInt32(buffer, 4);
            var ids = new int[count];
            for(var i = 0; i < count; i++) ids[i] = Marshal.ReadIntPtr(buffer, 8 + i * IntPtr.Size).ToInt32();
            return ids;
         }
         finally { Marshal.FreeHGlobal(buffer); }
      }
   }

   public void Terminate()
   {
      if(!TerminateJobObject(job, 1)) throw new Win32Exception();
   }

   public void Dispose() => job.Dispose();

   [StructLayout(LayoutKind.Sequential)] struct BasicLimits
   {
      public long ProcessTime, JobTime;
      public uint Flags;
      public UIntPtr MinWorkingSet, MaxWorkingSet;
      public uint ProcessLimit;
      public UIntPtr Affinity;
      public uint Priority, Scheduling;
   }
   [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits
   {
      public BasicLimits Basic;
      public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;
      public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
   }
   [StructLayout(LayoutKind.Sequential)] struct MemoryStatus
   {
      public uint Length, Load;
      public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtended;
   }
   [StructLayout(LayoutKind.Sequential)] struct StartupInfo
   {
      public int Size;
      public IntPtr Reserved, Desktop, Title;
      public int X, Y, SizeX, SizeY, CountX, CountY, Fill, Flags;
      public short Show, ReservedSize;
      public IntPtr ReservedBytes, Input, Output, Error;
   }
   [StructLayout(LayoutKind.Sequential)] struct ProcessInfo
   {
      public IntPtr Process, Thread;
      public int Pid, Tid;
   }

   [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)]
   static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string name);
   [DllImport("kernel32", SetLastError=true)]
   static extern bool SetInformationJobObject(SafeFileHandle job, int kind, ref ExtendedLimits data, int size);
   [DllImport("kernel32", SetLastError=true)]
   static extern bool QueryInformationJobObject(SafeFileHandle job, int kind, IntPtr data, int size, IntPtr returned);
   [DllImport("kernel32", CharSet=CharSet.Unicode, SetLastError=true)]
   static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
                                    bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo process);
   [DllImport("kernel32", SetLastError=true)] static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeFileHandle process);
   [DllImport("kernel32", SetLastError=true)] static extern uint ResumeThread(SafeFileHandle thread);
   [DllImport("kernel32", SetLastError=true)] static extern bool TerminateProcess(SafeFileHandle process, uint code);
   [DllImport("kernel32", SetLastError=true)] static extern bool TerminateJobObject(SafeFileHandle job, uint code);
   [DllImport("kernel32", SetLastError=true)] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
