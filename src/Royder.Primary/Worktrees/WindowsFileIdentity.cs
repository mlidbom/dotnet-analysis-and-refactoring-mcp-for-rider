using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Royder.Primary.Worktrees;

/// <summary>The volume and NTFS file ID, for both files and directories. A checkout and its Git directory
/// keep their identities across commits and branch switches, even when Git replaces HEAD or the index.</summary>
static class WindowsFileIdentity
{
   public static string Read(string path)
   {
      const uint readAttributes = 0x80, openDirectoryOrFile = 0x02000000;
      using var handle = CreateFileW(path, readAttributes, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero,
                                     FileMode.Open, openDirectoryOrFile, IntPtr.Zero);
      if(handle.IsInvalid)
      {
         var error = Marshal.GetLastWin32Error();
         if(error is 2 or 3) throw new FileNotFoundException("Git worktree identity disappeared.", path);
         throw new System.ComponentModel.Win32Exception(error);
      }
      if(!GetFileInformationByHandle(handle, out var info)) throw new System.ComponentModel.Win32Exception();
      return $"{info.Volume:x8}/{info.IndexHigh:x8}{info.IndexLow:x8}";
   }

   [StructLayout(LayoutKind.Sequential)]
   struct FileInformation
   {
      public uint Attributes;
      public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
      public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
   }

   [DllImport("kernel32.dll", SetLastError = true)]
   static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

   [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
   static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, IntPtr security, FileMode mode, uint flags, IntPtr template);
}
