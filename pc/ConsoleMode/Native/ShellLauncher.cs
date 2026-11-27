using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using ConsoleMode.Core;

namespace ConsoleMode.Native;

/// <summary>
/// Opens a program or URL as if the user double-clicked it: the desktop shell (explorer.exe) creates the process.
/// </summary>
/// <remarks>
/// Console Mode usually starts from a scheduled task, and Task Scheduler puts the task and every process it starts in
/// one job object. A frontend started by Console Mode would inherit that job, and some launchers do not run inside a
/// foreign job: Epic Games Launcher, started by Playnite to run an Epic game, quit without showing anything. Asking
/// the desktop's shell view to run it gives the new process explorer's parent, token and job instead.
/// <c>Shell.Application</c> created by us would not help: it runs in our own process, so its children stay in the job.
/// </remarks>
public static class ShellLauncher
{
    /// <summary>How long to wait for the desktop to exist, e.g. when the phone switches mode right after logon.</summary>
    private static readonly TimeSpan ShellReadyTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long the shell gets to run the program before falling back to starting it directly.</summary>
    private static readonly TimeSpan ShellCallTimeout = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Opens <paramref name="target"/> through the desktop shell, or with <see cref="Process.Start(ProcessStartInfo)"/>
    /// when the shell is not available. Only throws if that direct start fails. Blocks for a few seconds at most when
    /// the shell is missing or hung, so it must not run on the UI thread.
    /// </summary>
    public static void Open(string name, string target, string arguments, string workingDirectory)
    {
        if (TryOpenThroughShell(target, arguments, workingDirectory, out var reason))
        {
            Log.Info($"Opening {name} ({target}) through the desktop shell");
            return;
        }

        Log.Info($"{reason}; opening {name} ({target}) directly");
        Process.Start(new ProcessStartInfo(target, arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = workingDirectory,
        });
    }

    /// <summary>Never throws: on failure returns false and why, so the caller can start the program directly.</summary>
    private static bool TryOpenThroughShell(string target, string arguments, string workingDirectory, out string reason)
    {
        var deadline = DateTime.UtcNow + ShellReadyTimeout;
        var shellWindow = GetShellWindow();
        while (shellWindow == IntPtr.Zero && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(RetryDelay);
            shellWindow = GetShellWindow();
        }
        if (shellWindow == IntPtr.Zero)
        {
            reason = "Desktop shell not available";
            return false;
        }

        // COM calls into a hung explorer would block until the timeout below; better to find out now.
        if (SendMessageTimeout(shellWindow, WM_NULL, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG | SMTO_BLOCK, 2000, out _) == IntPtr.Zero)
        {
            reason = "Desktop shell is not responding";
            return false;
        }

        // Lets explorer bring the new window to the front (we usually have the foreground right after a mode switch).
        GetWindowThreadProcessId(shellWindow, out var explorerPid);
        AllowSetForegroundWindow(explorerPid);

        // The shell objects are apartment-threaded and our caller is a thread-pool (MTA) thread.
        var outcome = ShellCallOutcome.NotFound;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                outcome = ShellExecuteFromDesktop(target, arguments, workingDirectory, deadline);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        })
        {
            IsBackground = true,
            Name = "ShellLauncher",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        // The thread may still be retrying until the desktop is registered (up to the deadline) before calling the shell.
        var stillWaiting = deadline - DateTime.UtcNow;
        if (!thread.Join(ShellCallTimeout + (stillWaiting > TimeSpan.Zero ? stillWaiting : TimeSpan.Zero)))
        {
            reason = "Desktop shell did not answer in time";
            return false;
        }
        if (error != null)
        {
            Log.Error("Could not open through the desktop shell", error);
            reason = "Desktop shell call failed";
            return false;
        }
        if (outcome == ShellCallOutcome.NotFound)
        {
            reason = "Desktop shell not available";
            return false;
        }

        reason = "";
        return true;
    }

    private enum ShellCallOutcome
    {
        NotFound,
        Started,
    }

    /// <summary>
    /// ShellWindows → desktop browser → its active view → the view's automation object → Shell.Application of
    /// explorer → ShellExecute. Runs on an STA thread.
    /// </summary>
    private static ShellCallOutcome ShellExecuteFromDesktop(string target, string arguments, string workingDirectory, DateTime deadline)
    {
        var shellWindows = (IShellWindows)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_ShellWindows, throwOnError: true)!)!;
        object? desktop = null;
        IShellBrowser? browser = null;
        IShellView? view = null;
        object? folderView = null;
        object? shellApplication = null;
        try
        {
            // Right after logon the desktop may not be registered yet even though its window exists.
            while (true)
            {
                object location = CSIDL_DESKTOP;
                object root = null!;
                desktop = shellWindows.FindWindowSW(ref location, ref root, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH);
                if (desktop != null || DateTime.UtcNow >= deadline)
                {
                    break;
                }
                Thread.Sleep(RetryDelay);
            }
            if (desktop == null)
            {
                return ShellCallOutcome.NotFound;
            }

            var topLevelBrowser = SID_STopLevelBrowser;
            var shellBrowserId = typeof(IShellBrowser).GUID;
            browser = (IShellBrowser)((IComServiceProvider)desktop).QueryService(ref topLevelBrowser, ref shellBrowserId);
            view = browser.QueryActiveShellView();
            var dispatchId = IID_IDispatch;
            folderView = view.GetItemObject(SVGIO_BACKGROUND, ref dispatchId);

            // IShellFolderViewDual.Application and IShellDispatch2.ShellExecute, late-bound through IDispatch.
            shellApplication = folderView.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, folderView, null);
            shellApplication!.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, shellApplication,
                new object[] { target, arguments, workingDirectory, "open", SW_SHOWNORMAL });
            return ShellCallOutcome.Started;
        }
        finally
        {
            foreach (var comObject in new[] { shellApplication, folderView, view, browser, desktop, shellWindows })
            {
                if (comObject != null && Marshal.IsComObject(comObject))
                {
                    Marshal.ReleaseComObject(comObject);
                }
            }
        }
    }

    // ---------- COM ----------

    private static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    private static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    private static readonly Guid IID_IDispatch = new("00020400-0000-0000-C000-000000000046");
    private const int CSIDL_DESKTOP = 0;
    private const int SWC_DESKTOP = 8;
    private const int SWFO_NEEDDISPATCH = 1;
    private const uint SVGIO_BACKGROUND = 0;
    private const int SW_SHOWNORMAL = 1;

    // The interfaces below are called through their vtables, so every method before the one we use must be declared,
    // in the order of exdisp.idl / shobjidl_core.idl. Those we never call are placeholders that only fill their slot.

    [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IShellWindows
    {
        void Count();
        void Item();
        void NewEnum();
        void Register();
        void RegisterPending();
        void Revoke();
        void OnNavigate();
        void OnActivated();

        [return: MarshalAs(UnmanagedType.IDispatch)]
        object? FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object location,
            [MarshalAs(UnmanagedType.Struct)] ref object locationRoot, int windowClass, out int hwnd, int options);
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(ref Guid service, ref Guid interfaceId);
    }

    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void InsertMenusSB();
        void SetMenuSB();
        void RemoveMenusSB();
        void SetStatusTextSB();
        void EnableModelessSB();
        void TranslateAcceleratorSB();
        void BrowseObject();
        void GetViewStateStream();
        void GetControlWindow();
        void SendControlMsg();

        IShellView QueryActiveShellView();
    }

    [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void GetWindow();
        void ContextSensitiveHelp();
        void TranslateAccelerator();
        void EnableModeless();
        void UIActivate();
        void Refresh();
        void CreateViewWindow();
        void DestroyViewWindow();
        void GetCurrentInfo();
        void AddPropertySheetPages();
        void SaveViewState();
        void SelectItem();

        [return: MarshalAs(UnmanagedType.IUnknown)]
        object GetItemObject(uint item, ref Guid interfaceId);
    }

    // ---------- user32 ----------

    private const uint WM_NULL = 0x0000;
    private const uint SMTO_BLOCK = 0x0001;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags,
        uint timeoutMs, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
