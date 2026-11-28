using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Royder.Primary.Hosting.Launcher;

/// <summary>
///   A file replaced by another in one step: how a writer publishes a record that other processes read, such as a
///   private Rider run's <c>state.json</c>. The launcher compiles it with <c>Add-Type</c>, and the primary links it.
/// </summary>
/// <remarks>
///   <see cref="File.Move(string, string, bool)"/> fails with "Access to the path is denied" while any process has the
///   target open, even one that shares delete. On 2026-11-27 something held <c>state.json</c> through two seconds of
///   retries, and the supervisor failed a private Rider's start. Renamed with POSIX semantics, the target is replaced
///   while readers that share delete have it open, and they go on reading the copy they opened. A reader that doesn't
///   share delete still blocks it.
/// </remarks>
public static class FileReplacement
{
   /// <summary>Moves <paramref name="replacement"/> to <paramref name="target"/>, replacing the file there.</summary>
   /// <exception cref="IOException">With the Windows error as its <see cref="Exception.HResult"/>'s low word.</exception>
   public static void Replace(string replacement, string target)
   {
      using var file = CreateFileW(replacement, Delete | Synchronize, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
      if(file.IsInvalid) throw Failure($"Couldn't open {replacement} to move it");
      var name = Path.GetFullPath(target);
      var nameOffset = Marshal.OffsetOf<RenameHeader>(nameof(RenameHeader.FileNameLength)).ToInt32() + sizeof(uint);
      var size = nameOffset + (name.Length + 1) * sizeof(char);
      var buffer = Marshal.AllocHGlobal(size);
      try
      {
         Marshal.StructureToPtr(new RenameHeader { Flags = ReplaceIfExists | PosixSemantics, FileNameLength = (uint)(name.Length * sizeof(char)) }, buffer, false);
         Marshal.Copy(name.ToCharArray(), 0, buffer + nameOffset, name.Length);
         Marshal.WriteInt16(buffer, size - sizeof(char), 0);
         if(!SetFileInformationByHandle(file, FileRenameInfoEx, buffer, (uint)size)) throw Failure($"Couldn't replace {target}");
      }
      finally { Marshal.FreeHGlobal(buffer); }
   }

   static IOException Failure(string what)
   {
      var error = Marshal.GetLastWin32Error();
      return new IOException($"{what}: {new Win32Exception(error).Message}", unchecked((int)0x80070000 | (error & 0xFFFF)));
   }

   /// <summary>The fixed part of Windows' <c>FILE_RENAME_INFO</c>, which the new name follows.</summary>
   [StructLayout(LayoutKind.Sequential)]
   struct RenameHeader
   {
      public uint Flags;
      public IntPtr RootDirectory;
      public uint FileNameLength;
   }

   const int FileRenameInfoEx = 22;
   const uint ReplaceIfExists = 0x1, PosixSemantics = 0x2;
   const uint Delete = 0x00010000, Synchronize = 0x00100000;

   [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
   static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);

   [DllImport("kernel32.dll", SetLastError = true)]
   static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, IntPtr information, uint size);
}
