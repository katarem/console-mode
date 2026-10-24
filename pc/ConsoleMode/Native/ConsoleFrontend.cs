using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ConsoleMode.Core;
using Microsoft.Win32;

namespace ConsoleMode.Native;

/// <summary>Programa que se abre en la TV al entrar en modo consola.</summary>
public enum FrontendKind
{
    SteamBigPicture,
    Playnite,
    Custom,
}

/// <summary>
/// El «frontal» del modo consola: Steam Big Picture, Playnite en pantalla completa u otro programa (LaunchBox Big Box,
/// EmulationStation, un launcher propio...). Se abre al entrar en modo consola, se coloca en la pantalla de la consola y
/// se cierra al volver a modo PC.
/// </summary>
public static class ConsoleFrontend
{
    public const string PlayniteExe = "Playnite.FullscreenApp.exe";

    private static AppSettings? _settings;

    public static void Init(AppSettings settings) => _settings = settings;

    private static FrontendKind Kind => _settings?.Frontend ?? FrontendKind.SteamBigPicture;

    /// <summary>Si se abre al entrar en modo consola (si no, el modo consola solo cambia pantalla y audio).</summary>
    public static bool LaunchesOnConsoleMode => _settings?.LaunchFrontend ?? true;

    /// <summary>Nombre para mostrar: «Big Picture», «Playnite» o el del programa elegido.</summary>
    public static string Name => Kind switch
    {
        FrontendKind.Playnite => "Playnite",
        FrontendKind.Custom => ExePath is { Length: > 0 } path ? Path.GetFileNameWithoutExtension(path) : "el frontal",
        _ => "Big Picture",
    };

    /// <summary>Ejecutable del frontal (no se usa con Big Picture).</summary>
    private static string? ExePath => Kind switch
    {
        FrontendKind.Playnite => string.IsNullOrWhiteSpace(_settings?.PlaynitePath) ? DetectPlaynite() : _settings.PlaynitePath,
        FrontendKind.Custom => _settings?.CustomFrontendPath,
        _ => null,
    };

    /// <summary>Procesos del frontal: el overlay del mando recupera el foco si se lo quitan ellos.</summary>
    public static IReadOnlyCollection<string> ProcessNames => Kind == FrontendKind.SteamBigPicture
        ? new[] { "steam", "steamwebhelper", "gameoverlayui" }
        : ExePath is { Length: > 0 } path ? new[] { Path.GetFileNameWithoutExtension(path) } : Array.Empty<string>();

    public static void Open()
    {
        if (Kind == FrontendKind.SteamBigPicture)
        {
            SteamBigPicture.Open();
            return;
        }

        var exe = ExePath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            throw new InvalidOperationException(Kind == FrontendKind.Playnite
                ? "No se encuentra Playnite. Instálalo o elige Playnite.FullscreenApp.exe en la pestaña Modos."
                : "No se encuentra el programa del frontal. Elígelo en la pestaña Modos.");
        }

        var window = FindProgramWindow(exe);
        if (window != IntPtr.Zero)
        {
            Log.Info($"{Name} ya estaba abierto; se pone delante");
            ShowWindow(window, SW_RESTORE);
            SetForegroundWindow(window);
            return;
        }

        Log.Info($"Abriendo {Name} ({exe})");
        Process.Start(new ProcessStartInfo(exe, Kind == FrontendKind.Custom ? _settings?.CustomFrontendArgs ?? "" : "")
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
        });
    }

    public static async Task CloseAsync()
    {
        if (Kind == FrontendKind.SteamBigPicture)
        {
            await SteamBigPicture.CloseAsync();
            return;
        }

        var exe = ExePath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }
        if (!IsRunning(exe))
        {
            return;
        }

        // Como si se cerrase la ventana (Alt+F4): el programa guarda lo que necesite y se cierra.
        Log.Info($"Cerrando {Name}");
        foreach (var hwnd in ProgramWindows(exe))
        {
            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (IsRunning(exe))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new InvalidOperationException($"{Name} no se ha cerrado.");
            }
            await Task.Delay(250);
        }
    }

    public static bool IsOpen => Kind == FrontendKind.SteamBigPicture
        ? SteamBigPicture.IsOpen
        : ExePath is { Length: > 0 } exe && FindProgramWindow(exe) != IntPtr.Zero;

    /// <summary>Espera a que aparezca la ventana del frontal y la lleva a la pantalla indicada si no está ya.</summary>
    public static Task EnsureOnDisplayAsync(string displayId, TimeSpan timeout, CancellationToken ct = default)
    {
        if (Kind == FrontendKind.SteamBigPicture)
        {
            return SteamBigPicture.EnsureOnDisplayAsync(displayId, timeout, ct);
        }
        var exe = ExePath;
        return string.IsNullOrWhiteSpace(exe)
            ? Task.CompletedTask
            : WindowPlacement.EnsureOnDisplayAsync(() => FindProgramWindow(exe), Name, displayId, timeout, ct);
    }

    /// <summary>Playnite.FullscreenApp.exe en la carpeta de instalación de Playnite, o null si no está instalado.</summary>
    public static string? DetectPlaynite()
    {
        var folders = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Playnite"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Playnite"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Playnite"),
        };
        try
        {
            // Instalaciones en otra carpeta: la ruta está en la desinstalación de Windows.
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall");
                foreach (var name in uninstall?.GetSubKeyNames() ?? Array.Empty<string>())
                {
                    using var app = uninstall!.OpenSubKey(name);
                    if (app?.GetValue("DisplayName") is string display && display.StartsWith("Playnite", StringComparison.OrdinalIgnoreCase) &&
                        app.GetValue("InstallLocation") is string location && location.Length > 0)
                    {
                        folders.Insert(0, location);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo buscar Playnite en el registro", ex);
        }

        return folders.Select(f => Path.Combine(f, PlayniteExe)).FirstOrDefault(File.Exists);
    }

    // ---------- Ventanas de un programa ----------

    private static List<Process> ProgramProcesses(string exe) =>
        Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)).ToList();

    private static bool IsRunning(string exe)
    {
        var processes = ProgramProcesses(exe);
        processes.ForEach(p => p.Dispose());
        return processes.Count > 0;
    }

    /// <summary>Ventanas visibles de primer nivel del programa.</summary>
    private static List<IntPtr> ProgramWindows(string exe)
    {
        var pids = new HashSet<uint>();
        foreach (var p in ProgramProcesses(exe))
        {
            pids.Add((uint)p.Id);
            p.Dispose();
        }
        var windows = new List<IntPtr>();
        if (pids.Count == 0)
        {
            return windows;
        }
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pids.Contains(pid) && IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero &&
                (GetWindowLong(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) == 0)
            {
                windows.Add(hwnd);
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    /// <summary>
    /// La ventana principal del programa: la más grande de las que ocupan al menos media pantalla. Así no se toma por la
    /// principal la ventana de carga que muchos frontales (Playnite, por ejemplo) enseñan antes de la pantalla completa.
    /// </summary>
    private static IntPtr FindProgramWindow(string exe) => ProgramWindows(exe)
        .Select(hwnd =>
        {
            if (!GetWindowRect(hwnd, out var r))
            {
                return (hwnd, Area: 0L, Share: 0.0);
            }
            var area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            var screen = Screen.FromHandle(hwnd).Bounds;
            return (hwnd, Area: area, Share: area / (double)Math.Max(1L, (long)screen.Width * screen.Height));
        })
        .Where(w => w.Share >= 0.5)
        .OrderByDescending(w => w.Area)
        .Select(w => w.hwnd)
        .FirstOrDefault();

    private const uint WM_CLOSE = 0x0010;
    private const int SW_RESTORE = 9;
    private const uint GW_OWNER = 4;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowPlacement.RECT rect);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}

/// <summary>Lleva la ventana de un frontal a la pantalla de la consola, ocupándola entera.</summary>
public static class WindowPlacement
{
    /// <summary>
    /// Vigila la ventana del frontal hasta que esté en la pantalla indicada, ocupándola entera, durante 3 comprobaciones
    /// seguidas (o hasta <paramref name="timeout"/>). Si no está ahí, la lleva. El frontal se abre en la pantalla
    /// principal, así que normalmente ya estará bien; esto cubre los programas que recuerdan otra pantalla, los que
    /// enseñan primero una ventana de carga y los que se abren maximizados en un monitor concreto (Playnite).
    /// </summary>
    public static async Task EnsureOnDisplayAsync(Func<IntPtr> findWindow, string name, string displayId, TimeSpan timeout,
        CancellationToken ct = default)
    {
        const int MaxMoves = 5;
        var deadline = DateTime.UtcNow + timeout;
        var stable = 0;
        var moves = 0;
        var seen = false;
        var lastWindow = IntPtr.Zero;

        while (DateTime.UtcNow < deadline)
        {
            var hwnd = findWindow();
            if (hwnd == IntPtr.Zero)
            {
                await Task.Delay(500, ct);
                continue;
            }
            if (!seen)
            {
                seen = true;
                // Algunos programas se recolocan justo después de crear la ventana: se les deja un momento.
                await Task.Delay(1500, ct);
                continue;
            }

            var display = DisplayManager.Find(displayId);
            if (display == null)
            {
                return;
            }

            if (hwnd != lastWindow)
            {
                stable = 0; // ventana nueva (p. ej. tras la de carga): hay que comprobarla otra vez
                lastWindow = hwnd;
            }

            if (IsOnDisplay(hwnd, display.Bounds))
            {
                if (++stable >= 3)
                {
                    break;
                }
            }
            else if (moves < MaxMoves)
            {
                stable = 0;
                moves++;
                Log.Info($"Moviendo {name} a {display.FriendlyName}");
                MoveToDisplay(hwnd, display.Bounds);
            }
            else
            {
                Log.Info($"{name} vuelve a otra pantalla después de moverlo {MaxMoves} veces: elige la pantalla de la " +
                         "consola en los ajustes del propio programa");
                return;
            }

            await Task.Delay(1000, ct);
        }

        if (!seen)
        {
            Log.Info($"No se encontró la ventana de {name} para recolocarla");
            return;
        }

        var window = findWindow();
        if (window != IntPtr.Zero)
        {
            SetForegroundWindow(window);
        }
    }

    private static bool IsOnDisplay(IntPtr hwnd, Rectangle bounds)
    {
        if (!GetWindowRect(hwnd, out var rect))
        {
            return false;
        }
        var current = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var center = new Point(current.Left + current.Width / 2, current.Top + current.Height / 2);
        return bounds.Contains(center) && current.Width >= bounds.Width - 2;
    }

    /// <summary>
    /// Lleva la ventana al monitor y la ajusta a él. Una ventana maximizada se restaura, se mueve y se vuelve a
    /// maximizar, para que Windows la maximice en el monitor nuevo (moverla maximizada no funciona bien).
    /// </summary>
    private static void MoveToDisplay(IntPtr hwnd, Rectangle bounds)
    {
        var maximized = IsZoomed(hwnd);
        ShowWindow(hwnd, SW_RESTORE);
        SetWindowPos(hwnd, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOZORDER | SWP_SHOWWINDOW | SWP_FRAMECHANGED);
        if (maximized)
        {
            ShowWindow(hwnd, SW_MAXIMIZE);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const int SW_RESTORE = 9;
    private const int SW_MAXIMIZE = 3;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_FRAMECHANGED = 0x0020;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
