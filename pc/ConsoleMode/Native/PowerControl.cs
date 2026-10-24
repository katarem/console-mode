using System.Diagnostics;
using System.Runtime.InteropServices;
using ConsoleMode.Core;

namespace ConsoleMode.Native;

/// <summary>Suspender o apagar el PC (lo pide el móvil). Desde suspensión el Wake-on-LAN es más fiable que desde apagado.</summary>
public static class PowerControl
{
    public static void Sleep()
    {
        Log.Info("Suspendiendo el PC");
        // bHibernate = false: suspensión (S3 o la moderna); no hiberna aunque la hibernación esté activada.
        if (!SetSuspendState(false, false, false))
        {
            throw new InvalidOperationException($"Windows no ha dejado suspender el PC (error {Marshal.GetLastWin32Error()}).");
        }
    }

    public static void Shutdown()
    {
        Log.Info("Apagando el PC");
        // Apagado completo (/s sin /hybrid): con el inicio rápido, el apagado híbrido puede impedir el Wake-on-LAN.
        Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0") { UseShellExecute = false, CreateNoWindow = true });
    }

    // BOOLEAN de Win32: un byte.
    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
        [MarshalAs(UnmanagedType.U1)] bool forceCritical, [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);
}
