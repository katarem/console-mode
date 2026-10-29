using System.Runtime.InteropServices;
using ConsoleMode.Core;

namespace ConsoleMode.Native;

/// <summary>
/// Qué procesos son «de jugar»: el frontal, las tiendas de juegos y todo lo que abran ellos (los juegos son procesos
/// hijos suyos). Lo usan el modo consola inmersivo, para saber qué dejar ver, y el overlay, para volver al juego cuando
/// otra ventana se ha puesto delante.
/// </summary>
public static class GameProcesses
{
    /// <summary>
    /// Tiendas y lanzadores de juegos: se permiten ellos y todo lo que abran. El frontal elegido y los programas que
    /// añada el usuario se suman a estos.
    /// </summary>
    public static readonly string[] DefaultAllowedPrograms =
    {
        "steam", "steamwebhelper", "gameoverlayui", "Playnite.FullscreenApp", "Playnite.DesktopApp", "EpicGamesLauncher",
        "EpicWebHelper", "EADesktop", "EALauncher", "Origin", "upc", "UbisoftConnect", "Battle.net", "GalaxyClient",
        "XboxPcApp", "gamelaunchhelper", "LaunchBox", "BigBox", "RetroArch",
    };

    private static AppSettings? _settings;

    public static void Init(AppSettings settings) => _settings = settings;

    /// <summary>Programas (sin «.exe») que son «de jugar», junto con todo lo que abran.</summary>
    public static IEnumerable<string> AllowedPrograms()
    {
        var extra = new List<string>();
        if (_settings is { } settings)
        {
            lock (settings)
            {
                extra.AddRange(settings.ImmersiveAllowedPrograms);
            }
        }
        return DefaultAllowedPrograms.Concat(ConsoleFrontend.ProcessNames).Concat(extra);
    }

    private static ProcessTree? _tracked;
    private static System.Windows.Forms.Timer? _trackTimer;

    /// <summary>
    /// En modo consola se apunta cada segundo qué procesos han abierto el frontal y las tiendas, para seguir
    /// reconociendo un juego aunque el lanzador que lo abrió se cierre enseguida. Hay que llamarlo en el hilo de UI.
    /// </summary>
    public static void SetTracking(bool enabled)
    {
        if (enabled)
        {
            TrackedTree();
        }
        else if (_tracked != null)
        {
            _trackTimer?.Dispose();
            _trackTimer = null;
            _tracked = null;
        }
    }

    /// <summary>La lista que se mantiene al día (empieza a mantenerla si no estaba en marcha). Solo en el hilo de UI.</summary>
    internal static ProcessTree TrackedTree()
    {
        if (_tracked == null)
        {
            _tracked = new ProcessTree();
            _tracked.Refresh(AllowedPrograms());
            _trackTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _trackTimer.Tick += (_, _) => _tracked?.Refresh(AllowedPrograms());
            _trackTimer.Start();
        }
        return _tracked;
    }

    /// <summary>
    /// Comprobador de procesos para una consulta: en modo consola usa la lista que se mantiene al día; si no, mira una
    /// vez quién ha abierto a quién. Un proceso que aún no estaba en la lista hace que se vuelva a mirar.
    /// </summary>
    public static Func<int, bool> Checker()
    {
        var tree = _tracked;
        if (tree == null)
        {
            tree = new ProcessTree();
            tree.Refresh(AllowedPrograms());
        }
        return pid =>
        {
            if (tree.NameOf(pid) == null)
            {
                tree.Refresh(AllowedPrograms());
            }
            return tree.IsAllowed(pid);
        };
    }

    /// <summary>La ventana es del frontal, de una tienda o de un juego.</summary>
    public static bool IsGameWindow(IntPtr hwnd, Func<int, bool> isGame)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }
        GetWindowThreadProcessId(hwnd, out var pid);
        return pid != 0 && isGame((int)pid);
    }

    /// <summary>
    /// La ventana de juego (o del frontal) más alta de las que hay en pantalla: visible, sin minimizar y ocupando al
    /// menos media pantalla, como un juego o Big Picture. IntPtr.Zero si no hay ninguna.
    /// </summary>
    public static IntPtr FindGameWindow(Func<int, bool> isGame)
    {
        var found = IntPtr.Zero;
        var ownPid = (uint)Environment.ProcessId;
        // EnumWindows recorre las ventanas de arriba abajo: la primera que vale es la que estaba más delante.
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero ||
                (GetWindowLong(hwnd, GWL_STYLE) & WS_CHILD) != 0 || IsCloaked(hwnd) ||
                !GetWindowRect(hwnd, out var r))
            {
                return true;
            }
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0 || pid == ownPid || !isGame((int)pid))
            {
                return true;
            }
            var screen = Screen.FromHandle(hwnd).Bounds;
            var area = (long)(r.Right - r.Left) * (r.Bottom - r.Top);
            if (area * 2 < (long)screen.Width * screen.Height)
            {
                return true;
            }
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Ventanas que Windows mantiene ocultas aunque digan ser visibles (apps suspendidas, otros escritorios).</summary>
    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private const uint GW_OWNER = 4;
    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;
    private const int DWMWA_CLOAKED = 14;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowPlacement.RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}

/// <summary>
/// Procesos en marcha y quién abrió a quién, para saber si una ventana es de un juego: los programas permitidos y todo lo
/// que desciende de ellos. Un proceso que se apunta como permitido lo sigue siendo aunque se cierre el que lo abrió.
/// </summary>
internal sealed class ProcessTree
{
    private Dictionary<int, string> _names = new();

    /// <summary>Procesos permitidos y su hora de inicio (0 si no se puede saber), para no confundirlos si Windows reutiliza el pid.</summary>
    private readonly Dictionary<int, long> _allowed = new();

    public string? NameOf(int pid) => _names.TryGetValue(pid, out var name) ? name : null;

    public bool IsAllowed(int pid) => _allowed.ContainsKey(pid);

    public void Refresh(IEnumerable<string> allowedPrograms)
    {
        var roots = new HashSet<string>(allowedPrograms.Where(p => p.Length > 0), StringComparer.OrdinalIgnoreCase);
        var processes = Snapshot();
        _names = processes.ToDictionary(p => p.Pid, p => p.Name);

        // Fuera los que se han cerrado y los pid que ya son de otro proceso.
        foreach (var (pid, start) in _allowed.ToList())
        {
            if (!_names.ContainsKey(pid) || (start != 0 && StartTime(pid) is var now && now != 0 && now != start))
            {
                _allowed.Remove(pid);
            }
        }

        // Los programas permitidos y sus descendientes. Un hijo puede salir antes que su padre en la lista: se repite
        // hasta que no se añade ninguno.
        bool added;
        do
        {
            added = false;
            foreach (var p in processes)
            {
                if (_allowed.ContainsKey(p.Pid))
                {
                    continue;
                }
                if (roots.Contains(p.Name))
                {
                    _allowed[p.Pid] = StartTime(p.Pid);
                    added = true;
                }
                else if (_allowed.TryGetValue(p.Parent, out var parentStart))
                {
                    // Si el padre empezó después que él, el pid del padre es de otro proceso que lo ha reutilizado.
                    var start = StartTime(p.Pid);
                    if (parentStart == 0 || start == 0 || start >= parentStart)
                    {
                        _allowed[p.Pid] = start;
                        added = true;
                    }
                }
            }
        } while (added);
    }

    private static List<(int Pid, int Parent, string Name)> Snapshot()
    {
        var list = new List<(int, int, string)>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
        if (snapshot == INVALID_HANDLE_VALUE)
        {
            return list;
        }
        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            for (var ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
            {
                list.Add(((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, Path.GetFileNameWithoutExtension(entry.szExeFile)));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }
        return list;
    }

    private static long StartTime(int pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (process == IntPtr.Zero)
        {
            return 0;
        }
        try
        {
            return GetProcessTimes(process, out var creation, out _, out _, out _) ? creation : 0;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr snapshot, ref PROCESSENTRY32W entry);

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
