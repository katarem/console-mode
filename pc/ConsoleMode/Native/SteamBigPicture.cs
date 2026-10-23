using System.Diagnostics;
using System.Drawing;
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
    /// Espera a que aparezca la ventana de Big Picture y, si no está en el monitor indicado,
    /// la mueve y la ajusta a ese monitor. Big Picture se abre en la pantalla principal, así que
    /// normalmente ya estará bien colocada; esto cubre los casos en los que Steam recuerda otra pantalla.
    /// </summary>
    public static async Task EnsureOnDisplayAsync(string displayId, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        IntPtr hwnd;
        while ((hwnd = FindWindow()) == IntPtr.Zero)
        {
            if (DateTime.UtcNow > deadline)
            {
                Log.Info("No se encontró la ventana de Big Picture para recolocarla");
                return;
            }
            await Task.Delay(500, ct);
        }

        // Steam a veces se recoloca justo después de crear la ventana: comprobamos unas cuantas veces.
        for (var attempt = 0; attempt < 6; attempt++)
        {
            await Task.Delay(attempt == 0 ? 1500 : 1000, ct);
            hwnd = FindWindow();
            var display = DisplayManager.Find(displayId);
            if (hwnd == IntPtr.Zero || display == null)
            {
                return;
            }

            if (GetWindowRect(hwnd, out var rect))
            {
                var current = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                var center = new Point(current.Left + current.Width / 2, current.Top + current.Height / 2);
                if (display.Bounds.Contains(center) && current.Width >= display.Bounds.Width - 2)
                {
                    break;
                }
            }

            Log.Info($"Moviendo Big Picture a {display.FriendlyName}");
            ShowWindow(hwnd, SW_RESTORE);
            SetWindowPos(hwnd, IntPtr.Zero, display.Bounds.X, display.Bounds.Y, display.Bounds.Width, display.Bounds.Height,
                SWP_NOZORDER | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
        }

        hwnd = FindWindow();
        if (hwnd != IntPtr.Zero)
        {
            SetForegroundWindow(hwnd);
        }
    }

    public static bool IsOpen => FindWindow() != IntPtr.Zero;

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
    private const int SW_RESTORE = 9;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

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
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
