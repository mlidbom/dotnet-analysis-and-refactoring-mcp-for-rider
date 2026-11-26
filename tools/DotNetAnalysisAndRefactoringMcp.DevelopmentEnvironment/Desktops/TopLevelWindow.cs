using System.ComponentModel;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Desktops;

/// <summary>A window on a desktop that no other window owns, such as a Rider frame or a dialog of its own.</summary>
/// <remarks>
///   Windows lets a thread see and message only the windows of its own desktop: those of a <see cref="HiddenDesktop"/>
///   are reached in <see cref="HiddenDesktop.Run{T}"/>.
/// </remarks>
sealed class TopLevelWindow
{
   readonly HWND _handle;

   TopLevelWindow(HWND handle, string title, int processId)
   {
      _handle = handle;
      Title = title;
      ProcessId = processId;
   }

   /// <summary>Every visible top-level window on the calling thread's desktop, in its z-order, topmost first.</summary>
   /// <remarks>
   ///   The title and process are read as the windows are listed. A window that closes meanwhile, as Rider's splash
   ///   screen does, is left out instead of failing a later read.
   /// </remarks>
   public static IReadOnlyList<TopLevelWindow> AllVisible()
   {
      var windows = new List<TopLevelWindow>();
      EnumWindows((handle, _) =>
                  {
                     if(IsWindowVisible(handle) && GetWindowThreadProcessId(handle, out var processId) != 0)
                        windows.Add(new TopLevelWindow(handle, TitleOf(handle), (int)processId));
                     return true;
                  },
                  IntPtr.Zero);
      return windows;
   }

   /// <summary>The title the window had when it was listed.</summary>
   public string Title { get; }

   public int ProcessId { get; }

   /// <summary>The window's title and size, as the tools report them.</summary>
   public string Description
   {
      get
      {
         GetWindowRect(_handle, out var bounds);
         return $"'{Title}', {bounds.Width}x{bounds.Height}";
      }
   }

   /// <summary>
   ///   Asks the window to close, as its close button does, unless it has closed since it was listed. The application
   ///   decides what that means.
   /// </summary>
   public void AskToClose()
   {
      const int TheWindowIsGone = 1400; //ERROR_INVALID_WINDOW_HANDLE
      if(!PostMessage(_handle, (uint)WindowMessage.WM_CLOSE) && Marshal.GetLastPInvokeError() is var error and not TheWindowIsGone)
         throw new Win32Exception(error, $"Could not ask '{Title}' to close.");
   }

   static string TitleOf(HWND handle)
   {
      var title = new System.Text.StringBuilder(GetWindowTextLength(handle) + 1);
      var length = GetWindowText(handle, title, title.Capacity);
      return title.ToString(0, length);
   }
}
