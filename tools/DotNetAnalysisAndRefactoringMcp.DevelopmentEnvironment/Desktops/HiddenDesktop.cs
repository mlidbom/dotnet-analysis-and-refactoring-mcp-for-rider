using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Processes;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace DotNetAnalysisAndRefactoringMcp.DevelopmentEnvironment.Desktops;

/// <summary>
///   A desktop of Windows' that nobody sees, beside the one Magnus works on, for a <see cref="Riders.SandboxRider"/>: the
///   windows of what is started on it, and of all that starts, show there, so that they neither cover his nor take the
///   foreground from them.
/// </summary>
/// <remarks>
///   <para>
///     Not one of Windows' virtual desktops, which are all on the one Magnus sees, but a desktop of the window station
///     beside it, known by its name, which is gone once no process runs on it and none holds it open. Windows gives a
///     process the desktop of the one that started it, unless it is told another, which <see cref="ProcessStartInfo"/>
///     can't.
///   </para>
///   <para>
///     Magnus, 2026-11-26, after Cloyd's specifications had moved the windows they open to such a desktop: "could your
///     tests do that or something similar with the rider instance that they open?" Until then the sandbox's windows went
///     to the virtual desktop of the Rider the plugin is developed in, small, on its other monitor: in his sight, and each
///     new one took the foreground.
///   </para>
/// </remarks>
sealed class HiddenDesktop : IDisposable
{
   readonly SafeHDESK _desktop;

   HiddenDesktop(string name, SafeHDESK desktop)
   {
      Name = name;
      _desktop = desktop;
   }

   /// <summary>The desktop named <paramref name="name"/>, created unless it exists.</summary>
   public static HiddenDesktop Named(string name)
   {
      var desktop = OpenDesktop(name, 0, false, ACCESS_MASK.GENERIC_ALL);
      if(desktop.IsInvalid) desktop = CreateDesktop(name, 0, ACCESS_MASK.GENERIC_ALL, null);
      if(desktop.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not create the desktop {name}.");
      return new HiddenDesktop(name, desktop);
   }

   /// <summary>Its name in Windows' window station, which is the name of the sandbox it is for.</summary>
   public string Name { get; }

   /// <summary>
   ///   Starts <paramref name="program"/> on it, with <paramref name="arguments"/>, this process's environment, and none of
   ///   this process's handles.
   /// </summary>
   /// <remarks>
   ///   None of its handles: started with them, Rider inherited the console's, its output came out in the development
   ///   tools', and a pipe their output went into stayed open as long as Rider ran.
   /// </remarks>
   public Process Start(string program, IEnumerable<string> arguments)
   {
      var startup = new Kernel32.STARTUPINFO { cb = (uint)Marshal.SizeOf<Kernel32.STARTUPINFO>(), lpDesktop = $@"WinSta0\{Name}" };
      var commandLine = new StringBuilder(CommandLine.Of(program, arguments));
      if(!Kernel32.CreateProcess(null, commandLine, null, null, bInheritHandles: false, 0, null, null, startup, out var started))
         throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not start {commandLine} on the desktop {Name}.");
      using(started)
         return Process.GetProcessById((int)started.dwProcessId);
   }

   /// <summary>
   ///   Runs <paramref name="work"/> on a thread of its own on this desktop, where it can list the desktop's windows,
   ///   read them and post messages to them: Windows lets a thread do so only on its own desktop.
   /// </summary>
   public T Run<T>(Func<T> work)
   {
      var outcome = new TaskCompletionSource<T>();
      var thread = new Thread(() =>
      {
         try
         {
            if(!SetThreadDesktop(_desktop)) throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not put a thread on the desktop {Name}.");
            outcome.SetResult(work());
         }
         catch(Exception exception) // Handed to the calling thread, which rethrows it below.
         {
            outcome.SetException(exception);
         }
      });
      thread.Start();
      thread.Join();
      return outcome.Task.GetAwaiter().GetResult();
   }

   /// <summary>Lets go of it: it is gone once no process runs on it.</summary>
   public void Dispose() => _desktop.Dispose();
}
