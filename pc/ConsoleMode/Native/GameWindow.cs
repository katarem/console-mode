using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ConsoleMode.Core;

namespace ConsoleMode.Native;

/// <summary>
/// El juego que está delante al abrir el overlay del mando, para poder cerrarlo desde ahí: Playnite y otros frontales
/// no tienen, como Big Picture, un menú para salir del juego, y algunos juegos no traen botón de salir.
/// </summary>
public sealed class GameWindow
{
    // Ventanas que nunca son «el juego»: Windows, Steam, los frontales y la propia app.
    private static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost",
        "LockApp", "ApplicationFrameHost", "Taskmgr", "steam", "steamwebhelper", "gameoverlayui", "Playnite.DesktopApp",
        "Playnite.FullscreenApp",
    };

    private GameWindow(IntPtr handle, int processId, string name)
    {
        Handle = handle;
        ProcessId = processId;
        Name = name;
    }

    public IntPtr Handle { get; }
    public int ProcessId { get; }

    /// <summary>Título de la ventana o, si no tiene, el nombre del programa.</summary>
    public string Name { get; }

    /// <summary>El juego de esa ventana, o null si es el frontal, el escritorio u otra cosa que no es un juego.</summary>
    public static GameWindow? FromWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd))
        {
            return null;
        }
        GetWindowThreadProcessId(hwnd, out var pid);
        string process;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            process = p.ProcessName;
        }
        catch
        {
            return null;
        }

        if (pid == Environment.ProcessId || NotGames.Contains(process) ||
            ConsoleFrontend.ProcessNames.Contains(process, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        var title = new StringBuilder(256);
        GetWindowText(hwnd, title, title.Capacity);
        var name = title.ToString().Trim();
        return new GameWindow(hwnd, (int)pid, name.Length > 0 && name.Length <= 60 ? name : process);
    }

    public bool HasExited
    {
        get
        {
            try
            {
                using var p = Process.GetProcessById(ProcessId);
                return p.HasExited;
            }
            catch (ArgumentException)
            {
                return true; // ya no existe
            }
        }
    }

    /// <summary>Le pide al juego que se cierre, como Alt+F4.</summary>
    public void RequestClose()
    {
        Log.Info($"Cerrando el juego «{Name}» (pid {ProcessId})");
        if (!PostMessage(Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(error == ERROR_ACCESS_DENIED
                ? "Windows no deja cerrarlo: el juego se ejecuta como administrador."
                : $"No se pudo pedir al juego que se cierre (error {error}).");
        }
    }

    /// <summary>Termina el proceso del juego (y los que haya abierto él).</summary>
    public void Kill()
    {
        Log.Info($"Forzando el cierre del juego «{Name}» (pid {ProcessId})");
        try
        {
            using var p = Process.GetProcessById(ProcessId);
            p.Kill(entireProcessTree: true);
        }
        catch (ArgumentException)
        {
            // Ya se había cerrado.
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == ERROR_ACCESS_DENIED)
        {
            throw new InvalidOperationException("Windows no deja cerrarlo: el juego se ejecuta como administrador.");
        }
    }

    /// <summary>Espera a que el juego termine. Devuelve false si sigue abierto pasado el tiempo.</summary>
    public async Task<bool> WaitForExitAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!HasExited)
        {
            if (DateTime.UtcNow > deadline)
            {
                return false;
            }
            await Task.Delay(200);
        }
        return true;
    }

    private const uint WM_CLOSE = 0x0010;
    private const int ERROR_ACCESS_DENIED = 5;

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
