using System.Runtime.InteropServices;
using ConsoleMode.Core;

namespace ConsoleMode.Native;

/// <summary>
/// Modo consola inmersivo: en modo consola solo se ven el frontal, las tiendas de juegos, los juegos (y lo que abran
/// ellos) y el overlay. Cualquier otra ventana que aparezca o se ponga delante (Spotify, Discord, un actualizador...) se
/// oculta en ese mismo momento y el foco vuelve al juego. Al salir del modo se vuelven a mostrar.
/// </summary>
/// <remarks>
/// Windows no deja impedir que otro programa cree ventanas sin inyectar código en él (los antitrampas lo castigan), así
/// que se reacciona al aviso de Windows de que una ventana se ha mostrado o ha pasado a primer plano. El aviso llega en
/// milisegundos: lo normal es que la ventana no llegue a verse, aunque en algún caso puede asomar un instante.
/// Las ventanas que ya estaban abiertas al activarlo no se tocan mientras se queden detrás.
/// </remarks>
public sealed class ImmersiveGuard : IDisposable
{
    /// <summary>
    /// Tiendas y lanzadores de juegos: se permiten ellos y todo lo que abran (los juegos son procesos hijos suyos). El
    /// frontal elegido y los programas que añada el usuario se suman a estos.
    /// </summary>
    public static readonly string[] DefaultAllowedPrograms =
    {
        "steam", "steamwebhelper", "gameoverlayui", "Playnite.FullscreenApp", "Playnite.DesktopApp", "EpicGamesLauncher",
        "EpicWebHelper", "EADesktop", "EALauncher", "Origin", "upc", "UbisoftConnect", "Battle.net", "GalaxyClient",
        "XboxPcApp", "gamelaunchhelper", "LaunchBox", "BigBox", "RetroArch",
    };

    /// <summary>
    /// Partes de Windows y overlays que no se ocultan nunca (pero lo que abran ellos sí): el menú Inicio, el Administrador
    /// de tareas, la barra de juego, los overlays de NVIDIA y AMD, el aviso de que un programa ha fallado...
    /// </summary>
    private static readonly HashSet<string> SystemPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "csrss", "winlogon", "LogonUI", "LockApp", "consent", "ShellExperienceHost", "ShellHost",
        "StartMenuExperienceHost", "SearchHost", "SearchApp", "SearchUI", "TextInputHost", "ctfmon",
        "ApplicationFrameHost", "Taskmgr", "WerFault", "GameBar", "GameBarFTServer", "NVIDIA Overlay", "NVIDIA Share",
        "RadeonSoftware", "AMDRSServ",
    };

    /// <summary>Ventanas de carpeta del Explorador: el resto del Explorador (barra de tareas, escritorio...) no se toca.</summary>
    private static readonly HashSet<string> ExplorerFolderClasses = new(StringComparer.Ordinal) { "CabinetWClass", "ExploreWClass" };

    private enum Verdict
    {
        Ignore, // invisible, sin tamaño, de Windows o de Console Mode
        Game,   // el frontal, una tienda o un juego
        Hide,
    }

    private readonly Func<IEnumerable<string>> _allowedPrograms;
    private readonly ProcessTree _processes = new();
    private readonly HashSet<IntPtr> _hidden = new();
    private readonly WinEventProc _callback; // referencia viva para que el GC no la recoja
    private readonly IntPtr[] _hooks;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly System.Windows.Forms.Timer _focusTimer = new() { Interval = 150 };
    private IntPtr _lastGameWindow;

    /// <summary>Hay que crearlo en el hilo de UI: los avisos de Windows llegan por su cola de mensajes.</summary>
    /// <param name="allowedPrograms">Programas (sin «.exe») que se permiten, junto con todo lo que abran.</param>
    public ImmersiveGuard(Func<IEnumerable<string>> allowedPrograms)
    {
        _allowedPrograms = allowedPrograms;
        _processes.Refresh(_allowedPrograms());
        _callback = OnWinEvent;
        _hooks = new[]
        {
            SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS),
            SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS),
        };

        // Cada segundo se apuntan los procesos nuevos de los juegos, por si el que los abrió se cierra enseguida (un
        // lanzador que abre el juego y se va): sin esto el juego quedaría sin «padre» permitido.
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _refreshTimer.Tick += (_, _) =>
        {
            _processes.Refresh(_allowedPrograms());
            _hidden.RemoveWhere(hwnd => !IsWindow(hwnd));
        };
        _refreshTimer.Start();
        _focusTimer.Tick += (_, _) => RestoreGameFocus();

        Current = this;
        Log.Info("Modo consola inmersivo activado: solo se verán el frontal, los juegos y el overlay");

        // Si al activarlo hay delante una ventana que no toca (p. ej. Discord al iniciar sesión), se oculta ya.
        Check(GetForegroundWindow(), foreground: true);
    }

    /// <summary>El guardián activo, o null si el modo inmersivo no está en marcha.</summary>
    public static ImmersiveGuard? Current { get; private set; }

    /// <summary>
    /// La ventana es de las que el modo inmersivo oculta. El overlay lo usa para recuperar el foco en lugar de cerrarse
    /// cuando una de ellas se lo quita un momento.
    /// </summary>
    public static bool Hides(IntPtr hwnd) => Current?.Evaluate(hwnd) == Verdict.Hide;

    public void Dispose()
    {
        foreach (var hook in _hooks.Where(h => h != IntPtr.Zero))
        {
            UnhookWinEvent(hook);
        }
        _refreshTimer.Dispose();
        _focusTimer.Dispose();
        if (Current == this)
        {
            Current = null;
        }

        // Lo que se ocultó vuelve a verse, sin quitarle el foco a nadie.
        var restored = 0;
        foreach (var hwnd in _hidden.Where(IsWindow))
        {
            ShowWindowAsync(hwnd, SW_SHOWNA);
            restored++;
        }
        _hidden.Clear();
        Log.Info($"Modo consola inmersivo desactivado ({restored} ventanas vuelven a verse)");
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
        {
            return;
        }
        Check(hwnd, eventType == EVENT_SYSTEM_FOREGROUND);
    }

    private void Check(IntPtr hwnd, bool foreground)
    {
        switch (Evaluate(hwnd))
        {
            case Verdict.Hide:
                Hide(hwnd);
                break;
            case Verdict.Game when foreground:
                _lastGameWindow = hwnd;
                break;
        }
    }

    private Verdict Evaluate(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd) || (GetWindowLong(hwnd, GWL_STYLE) & WS_CHILD) != 0 ||
            !GetWindowRect(hwnd, out var rect) || rect.Right - rect.Left <= 1 || rect.Bottom - rect.Top <= 1)
        {
            return Verdict.Ignore;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0 || pid == _ownProcessId)
        {
            return Verdict.Ignore;
        }

        var name = _processes.NameOf((int)pid);
        if (name == null)
        {
            // Proceso que aún no estaba en la lista: se vuelve a mirar quién lo ha abierto.
            _processes.Refresh(_allowedPrograms());
            name = _processes.NameOf((int)pid);
            if (name == null)
            {
                return Verdict.Ignore; // ya se ha cerrado
            }
        }

        if (string.Equals(name, "explorer", StringComparison.OrdinalIgnoreCase))
        {
            return ExplorerFolderClasses.Contains(WindowFocus.ClassNameOf(hwnd)) ? Verdict.Hide : Verdict.Ignore;
        }
        if (SystemPrograms.Contains(name))
        {
            return Verdict.Ignore;
        }
        return _processes.IsAllowed((int)pid) ? Verdict.Game : Verdict.Hide;
    }

    private void Hide(IntPtr hwnd)
    {
        var wasForeground = GetForegroundWindow() == hwnd;
        ShowWindowAsync(hwnd, SW_HIDE);
        if (_hidden.Add(hwnd))
        {
            Log.Info($"Modo inmersivo: se oculta {WindowFocus.Describe(hwnd)}");
        }
        if (wasForeground && !_focusTimer.Enabled)
        {
            // Al ocultar la ventana activa, Windows suele activar la que había debajo (el juego). Si no, se le devuelve
            // el foco al juego o al frontal.
            _focusTimer.Start();
        }
    }

    private void RestoreGameFocus()
    {
        _focusTimer.Stop();
        // Si lo tiene un juego o Console Mode (el overlay), no se toca.
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var pid);
        if (Current != this || pid == _ownProcessId || Evaluate(foreground) == Verdict.Game)
        {
            return;
        }
        if (_lastGameWindow != IntPtr.Zero && IsWindow(_lastGameWindow) && IsWindowVisible(_lastGameWindow))
        {
            WindowFocus.Activate(_lastGameWindow);
        }
        else
        {
            ConsoleFrontend.Activate();
        }
    }

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    private const int OBJID_WINDOW = 0;
    private const int CHILDID_SELF = 0;
    private const int GWL_STYLE = -16;
    private const int WS_CHILD = 0x40000000;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8;

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback,
        uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowPlacement.RECT rect);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    // Asíncrono: si el programa está colgado, ShowWindow esperaría a que responda.
    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int cmdShow);
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
