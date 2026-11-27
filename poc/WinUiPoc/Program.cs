using System;
using System.IO;
using System.Threading;
using Microsoft.UI.Dispatching;

namespace WinUiPoc;

/// <summary>
/// WinUI 3 runs on its own STA thread (Application.Start) while WinForms keeps the process main thread, as the tray
/// icon and the gamepad overlay of Console Mode would.
/// </summary>
public static class Program
{
    public static System.Windows.Forms.Form? WinFormsForm { get; private set; }

    private static DispatcherQueue? _winUiQueue;

    [STAThread]
    public static void Main()
    {
        Thread.CurrentThread.Name = "WinForms";
        PocLog.Write("Process started");
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var winUiThread = new Thread(() =>
        {
            try
            {
                Microsoft.UI.Xaml.Application.Start(_ =>
                {
                    _winUiQueue = DispatcherQueue.GetForCurrentThread();
                    SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(_winUiQueue));
                    new App();
                });
                PocLog.Write("WinUI Application.Start returned");
            }
            catch (Exception ex)
            {
                PocLog.Write($"WinUI thread failed: {ex}");
            }
        })
        { Name = "WinUI" };
        winUiThread.SetApartmentState(ApartmentState.STA);
        winUiThread.Start();

        System.Windows.Forms.Application.EnableVisualStyles();
        WinFormsForm = new System.Windows.Forms.Form { Text = "WinUiPoc WinForms window", Width = 400, Height = 200 };
        var timer = new System.Windows.Forms.Timer { Interval = 3000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            // WinForms thread -> WinUI thread.
            var queued = _winUiQueue?.TryEnqueue(() => App.Window?.ShowFromWinForms("Hello from WinForms")) ?? false;
            PocLog.Write($"WinForms enqueued work on WinUI: {queued}");
        };
        WinFormsForm.Shown += (_, _) =>
        {
            PocLog.Write("WinForms window shown");
            timer.Start();
        };
        System.Windows.Forms.Application.Run(WinFormsForm);
    }
}

internal static class PocLog
{
    private static readonly string File = Path.Combine(Path.GetTempPath(), "winuipoc.log");
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        lock (Gate)
        {
            System.IO.File.AppendAllText(File, $"{DateTime.Now:HH:mm:ss.fff} [{Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString()}] {message}{Environment.NewLine}");
        }
    }
}
