using System.Runtime.InteropServices;

namespace ConsoleMode.Native;

/// <summary>
/// Windows no deja que una app en segundo plano se ponga delante sin más. El overlay lo necesita
/// para que los botones del mando no lleguen también a Big Picture mientras está abierto.
/// </summary>
public static class WindowFocus
{
    public static IntPtr Foreground => GetForegroundWindow();

    public static void ForceForeground(IntPtr hwnd)
    {
        var foreground = GetForegroundWindow();
        if (foreground == hwnd)
        {
            return;
        }

        // 1) Compartir la cola de entrada con la ventana que tiene el foco permite quitárselo.
        var foregroundThread = GetWindowThreadProcessId(foreground, out _);
        var ourThread = GetCurrentThreadId();
        if (foregroundThread != 0 && foregroundThread != ourThread)
        {
            AttachThreadInput(ourThread, foregroundThread, true);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            AttachThreadInput(ourThread, foregroundThread, false);
        }

        // 2) Si no ha bastado, una pulsación de Alt hace que Windows acepte el cambio de foco.
        if (GetForegroundWindow() != hwnd)
        {
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            SetForegroundWindow(hwnd);
        }
    }

    /// <summary>Devuelve el foco a la ventana que lo tenía (Big Picture o el juego).</summary>
    public static void Restore(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && IsWindow(hwnd))
        {
            SetForegroundWindow(hwnd);
        }
    }

    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extraInfo);
}
