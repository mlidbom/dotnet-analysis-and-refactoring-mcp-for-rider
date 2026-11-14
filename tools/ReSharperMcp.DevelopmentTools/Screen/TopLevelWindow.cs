using Vanara.PInvoke;
using static Vanara.PInvoke.User32;
using VirtualDesktop = WindowsDesktop.VirtualDesktop;

namespace ReSharperMcp.DevelopmentTools.Screen;

/// <summary>A window on the desktop that no other window owns, such as a Rider frame or a dialog of its own.</summary>
sealed class TopLevelWindow
{
  readonly HWND _handle;

  TopLevelWindow(HWND handle) => _handle = handle;

  /// <summary>Every visible top-level window, in the desktop's z-order, topmost first.</summary>
  public static IReadOnlyList<TopLevelWindow> AllVisible()
  {
    var windows = new List<TopLevelWindow>();
    EnumWindows((handle, _) =>
    {
      if (IsWindowVisible(handle)) windows.Add(new TopLevelWindow(handle));
      return true;
    }, IntPtr.Zero);
    return windows;
  }

  public string Title
  {
    get
    {
      var title = new System.Text.StringBuilder(GetWindowTextLength(_handle) + 1);
      GetWindowText(_handle, title, title.Capacity);
      return title.ToString();
    }
  }

  public int ProcessId
  {
    get
    {
      GetWindowThreadProcessId(_handle, out var processId);
      return (int)processId;
    }
  }

  /// <summary>The window's handle, as the virtual desktop API takes it.</summary>
  public IntPtr Handle => (IntPtr)_handle;

  /// <summary>The monitor that holds most of this window.</summary>
  public Monitor Monitor => Monitor.Holding(_handle);

  /// <summary>Where the window is, in physical pixels of the virtual screen.</summary>
  public RECT Bounds
  {
    get
    {
      GetWindowRect(_handle, out var bounds);
      return bounds;
    }
  }

  /// <summary>The virtual desktop the window is on, or null for a window pinned to all of them.</summary>
  public VirtualDesktop? Desktop => VirtualDesktop.FromHwnd((IntPtr)_handle);

  /// <summary>Moves this window to the virtual desktop <paramref name="other"/> is on.</summary>
  public void MoveToTheDesktopOf(TopLevelWindow other) =>
    VirtualDesktop.MoveToDesktop((IntPtr)_handle,
                                 other.Desktop ?? throw new InvalidOperationException($"'{other.Title}' is pinned to every virtual desktop, so it has no one desktop to move to."));

  /// <summary>The window's title, place and desktop, as the tools report them.</summary>
  public string Description
  {
    get
    {
      var bounds = Bounds;
      var desktopNumber = Desktop is { } desktop ? $"Desktop {Array.FindIndex(VirtualDesktop.GetDesktops(), it => it.Id == desktop.Id) + 1}" : "every desktop";
      return $"'{Title}' at ({bounds.left}, {bounds.top}), {bounds.Width}x{bounds.Height}, on {desktopNumber}";
    }
  }

  /// <summary>Restores the window if it's minimized or maximized, and gives it <paramref name="bounds"/>, in physical pixels.</summary>
  public void MoveTo(RECT bounds)
  {
    ShowWindow(_handle, ShowWindowCommand.SW_RESTORE);
    SetWindowPos(_handle, HWND.NULL, bounds.left, bounds.top, bounds.Width, bounds.Height,
                 SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);
  }

  /// <summary>Asks the window to close, as its close button does. The application decides what that means.</summary>
  public void AskToClose() => PostMessage(_handle, (uint)WindowMessage.WM_CLOSE);
}
