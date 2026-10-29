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

    private readonly HashSet<IntPtr> _hidden = new();
    private readonly WinEventProc _callback; // referencia viva para que el GC no la recoja
    private readonly IntPtr[] _hooks;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly System.Windows.Forms.Timer _focusTimer = new() { Interval = 150 };
    private IntPtr _lastGameWindow;

    /// <summary>
    /// Hay que crearlo en el hilo de UI: los avisos de Windows llegan por su cola de mensajes. Lo que se deja ver lo
    /// decide <see cref="GameProcesses"/>, que se mantiene al día mientras dure.
    /// </summary>
    public ImmersiveGuard()
    {
        _callback = OnWinEvent;
        _hooks = new[]
        {
            SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS),
            SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS),
        };

        // Se olvidan las ventanas ocultas que ya se han cerrado.
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _refreshTimer.Tick += (_, _) => _hidden.RemoveWhere(hwnd => !IsWindow(hwnd));
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

        var processes = GameProcesses.TrackedTree();
        var name = processes.NameOf((int)pid);
        if (name == null)
        {
            // Proceso que aún no estaba en la lista: se vuelve a mirar quién lo ha abierto.
            processes.Refresh(GameProcesses.AllowedPrograms());
            name = processes.NameOf((int)pid);
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
        return processes.IsAllowed((int)pid) ? Verdict.Game : Verdict.Hide;
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
