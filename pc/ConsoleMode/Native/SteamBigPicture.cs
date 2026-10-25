using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using ConsoleMode.Core;

namespace ConsoleMode.Native;

/// <summary>Abre, cierra y coloca la ventana de Steam Big Picture.</summary>
public static class SteamBigPicture
{
    public static void Open()
    {
        Log.Info("Abriendo Steam Big Picture");
        Process.Start(new ProcessStartInfo("steam://open/bigpicture") { UseShellExecute = true });
    }

    public static async Task CloseAsync()
    {
        if (!IsSteamRunning())
        {
            return;
        }

        // Aunque no encontremos la ventana, se lo pedimos a Steam: si Big Picture no está abierto no hace nada.
        Log.Info(IsOpen ? "Cerrando Steam Big Picture" : "Big Picture no detectado; se pide a Steam que lo cierre igualmente");
        try
        {
            Process.Start(new ProcessStartInfo("steam://close/bigpicture") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("steam://close/bigpicture falló", ex);
        }

        if (await WaitUntilClosedAsync(TimeSpan.FromSeconds(4)))
        {
            return;
        }

        // Si Steam no responde al protocolo, cerramos la ventana como haría Alt+F4.
        var hwnd = FindWindow();
        if (hwnd != IntPtr.Zero)
        {
            Log.Info("Steam no ha cerrado Big Picture; se envía WM_CLOSE a la ventana");
            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        if (!await WaitUntilClosedAsync(TimeSpan.FromSeconds(4)))
        {
            throw new InvalidOperationException("Steam no ha cerrado Big Picture.");
        }
    }

    private static async Task<bool> WaitUntilClosedAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (FindWindow() != IntPtr.Zero)
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }
            await Task.Delay(250);
        }
        return true;
    }

    private static bool IsSteamRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        foreach (var p in processes)
        {
            p.Dispose();
        }
        return processes.Length > 0;
    }

    /// <summary>
    /// Espera a que aparezca la ventana de Big Picture y, si no está en el monitor indicado, la mueve a ese monitor.
    /// Big Picture se abre en la pantalla principal; esto cubre los casos en los que Steam recuerda otra pantalla.
    /// </summary>
    public static Task EnsureOnDisplayAsync(string displayId, TimeSpan timeout, CancellationToken ct = default) =>
        WindowPlacement.EnsureOnDisplayAsync(FindWindow, "Big Picture", displayId, timeout, ct);

    public static bool IsOpen => FindWindow() != IntPtr.Zero;

    /// <summary>Pone Big Picture delante (p. ej. al cerrar un juego desde el overlay). Devuelve false si no está abierto.</summary>
    public static bool Activate()
    {
        var hwnd = FindWindow();
        return hwnd != IntPtr.Zero && SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    /// <summary>
    /// Busca la ventana principal de Big Picture: pertenece a steamwebhelper/steam y su título contiene
    /// "Big Picture" (en español: "Modo Big Picture de Steam"). Steam a veces separa las dos palabras con
    /// un espacio de no separación (U+00A0), así que se acepta cualquier separador Unicode.
    /// </summary>
    private static IntPtr FindWindow()
    {
        var steamPids = new HashSet<uint>();
        foreach (var name in new[] { "steamwebhelper", "steam" })
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                steamPids.Add((uint)p.Id);
                p.Dispose();
            }
        }
        if (steamPids.Count == 0)
        {
            return IntPtr.Zero;
        }

        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
            {
                return true;
            }
            GetWindowThreadProcessId(hwnd, out var pid);
            if (!steamPids.Contains(pid))
            {
                return true;
            }

            var title = new StringBuilder(256);
            GetWindowText(hwnd, title, title.Capacity);
            if (BigPictureTitle.IsMatch(title.ToString()))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static readonly Regex BigPictureTitle = new(@"Big\p{Z}+Picture", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private const uint WM_CLOSE = 0x0010;
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
