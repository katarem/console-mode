using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ConsoleMode.Core;
using ConsoleMode.Native;

namespace ConsoleMode.UI;

/// <summary>
/// Overlay para el mando en la esquina inferior derecha: cambia de modo, de salida de audio, de micrófono, de salida de
/// comunicaciones y de pantalla principal, ajusta o silencia el sonido y el micrófono, activa o desactiva el modo
/// inmersivo y suspende, reinicia o apaga el PC. La cabecera muestra la hora y la batería del mando que lo ha abierto.
/// Los cambios de audio y pantalla son temporales: no se guardan en los perfiles de los modos.
/// </summary>
/// <remarks>
/// Cada opción muestra solo lo elegido ahora; al pulsar A, el overlay pasa al centro de la pantalla con la lista de
/// opciones y, al elegir (o con B), vuelve a la esquina. Es la misma ventana, así que el foco no se pierde por el camino.
/// Se dibuja entero a mano (sin controles hijos) con el escalado (DPI) de la pantalla donde aparece.
/// Con controles normales, WinForms los reescalaba por su cuenta al llegar a una TV 4K al 200 % y el texto
/// salía pequeño, o al doble y cortado, hasta que se reconstruían.
/// </remarks>
public sealed class OverlayForm : Form
{
    private static readonly Color Background = Color.FromArgb(22, 22, 30);
    private static readonly Color Border = Color.FromArgb(58, 58, 76);
    private static readonly Color Surface = Color.FromArgb(34, 34, 46);
    private static readonly Color Accent = Color.FromArgb(88, 80, 236);
    private static readonly Color TextColor = Color.FromArgb(240, 240, 248);
    private static readonly Color MutedColor = Color.FromArgb(150, 150, 172);
    private static readonly Color ErrorColor = Color.FromArgb(255, 120, 120);
    private static readonly Color TrackColor = Color.FromArgb(70, 70, 92);

    // Color de cada tipo de opción (fondo del icono).
    private static readonly Color ConsoleColor = Color.FromArgb(34, 197, 94);
    private static readonly Color PcColor = Color.FromArgb(245, 158, 11);
    private static readonly Color AudioColor = Color.FromArgb(59, 130, 246);
    private static readonly Color MicColor = Color.FromArgb(236, 72, 153);
    private static readonly Color CommsColor = Color.FromArgb(139, 92, 246);
    private static readonly Color DisplayColor = Color.FromArgb(20, 184, 166);
    private static readonly Color MutedOnColor = Color.FromArgb(239, 68, 68);
    private static readonly Color MutedOffColor = Color.FromArgb(52, 52, 68);
    private static readonly Color CloseGameColor = Color.FromArgb(220, 38, 38);
    private static readonly Color SleepColor = Color.FromArgb(99, 102, 241);

    // Iconos de Segoe Fluent Icons (Windows 11) o Segoe MDL2 Assets (Windows 10): mismos códigos en las dos.
    private const string GlyphVolume = "\uE767";
    private const string GlyphMute = "\uE74F";
    private const string GlyphMic = "\uE720";
    private const string GlyphHeadset = "\uE95B";
    private const string GlyphDisplay = "\uE7F4";
    private const string GlyphGame = "\uE7FC";
    private const string GlyphPc = "\uE977";
    private const string GlyphChevron = "\uE76C";
    private const string GlyphCheck = "\uE73E";
    private const string GlyphClose = "\uE711";
    private const string GlyphPlay = "\uE768";
    private const string GlyphBack = "\uE72B";
    private const string GlyphSleep = "\uE708";
    private const string GlyphRestart = "\uE777";
    private const string GlyphPower = "\uE7E8";
    private const string GlyphImmersive = "\uE740";

    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(20);
    // The battery level changes slowly: it is read again every 30 ticks of the 1 s timer.
    private const int BatteryRefreshTicks = 30;
    // If Windows has not restarted or shut down by then (an app asking to save, for instance), the overlay gives control
    // back instead of staying stuck on «Apagando…».
    private static readonly TimeSpan PowerActionTimeout = TimeSpan.FromSeconds(15);
    private const int VolumeStep = 5;

    // PreserveGraphicsClipping: sin él, TextRenderer ignora el recorte del Graphics y el texto de las filas que
    // asoman por debajo de la lista se dibujaba encima del pie.
    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping;
    private const TextFormatFlags CenterFlags =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.PreserveGraphicsClipping;

    private enum RowKind
    {
        SwitchMode,
        ReturnToGame,
        CloseGame,
        MuteToggles,
        Picker,
        Volume,
        ImmersiveToggle,
        PowerActions,
    }

    /// <summary>What the centered «Sí / No» dialog asks for.</summary>
    private enum ConfirmKind
    {
        CloseGame,
        Sleep,
        Restart,
        Shutdown,
    }

    /// <summary>Buttons of the power row, left to right.</summary>
    private static readonly ConfirmKind[] PowerActions = { ConfirmKind.Sleep, ConfirmKind.Restart, ConfirmKind.Shutdown };

    private enum PickerKind
    {
        Audio,
        Microphone,
        Communications,
        Display,
    }

    /// <summary>Una opción de la vista principal.</summary>
    private sealed record Row(RowKind Kind, string Key, string Title, string Value, string Glyph, Color Color,
        PickerKind Picker = default, AudioFlow Flow = default, AppMode Target = default);

    private readonly System.Windows.Forms.Timer _idleTimer;
    private readonly System.Windows.Forms.Timer _focusTimer = new() { Interval = 100 };
    private readonly System.Windows.Forms.Timer _volumeTimer = new() { Interval = 1000 };
    private ForegroundWatcher? _foregroundWatcher;
    private readonly System.Diagnostics.Stopwatch _focusClock = new();
    private readonly System.Diagnostics.Stopwatch _openClock = System.Diagnostics.Stopwatch.StartNew();
    private readonly IntPtr _previousForeground;
    private readonly ModeSwitcher _switcher;
    private readonly Func<bool> _inputHeld;
    private readonly string _previousProcess;

    // Juego que estaba delante al abrir el overlay (null si era el frontal, el escritorio...). Cerrarlo pide confirmación
    // en un diálogo; si no se cierra por las buenas, se ofrece forzar el cierre.
    private readonly GameWindow? _game;
    private readonly IntPtr _minimizedGame;
    private bool _offerForceClose;

    // En modo consola, ventana del juego (o del frontal) a la que volver si al abrir el overlay había otra cosa delante
    // (Spotify, Discord, el escritorio...). IntPtr.Zero si delante ya estaba el juego.
    private readonly IntPtr _returnTarget;
    private bool _returnRequested;

    // Centered confirm dialog («¿Cerrar el juego?», «¿Apagar el PC?»...): what it asks (null when closed), its two
    // options and the selected one (it starts on «No»).
    private ConfirmKind? _confirm;
    private readonly List<Row> _confirmRows = new();
    private int _confirmSelected;
    private bool _gameClosed;
    private readonly System.Windows.Forms.Timer _releaseTimer = new() { Interval = 20 };
    private readonly System.Diagnostics.Stopwatch _releaseClock = new();
    private int _releasedPolls;
    private bool _closing;
    private bool _switchingToPc;
    private Fonts? _fonts;
    private string _status = "";
    private bool _statusIsError;
    private int _dpi;
    private bool _focusLost;
    private bool _focusFailureLogged;
    private long _lastPadActionAt;
    private bool _busy;
    private string? _targetDisplayGdiName;

    // Datos
    private readonly List<Row> _rows = new();
    private IReadOnlyList<QuickOption> _audio = Array.Empty<QuickOption>();
    private IReadOnlyList<QuickOption> _mics = Array.Empty<QuickOption>();
    private IReadOnlyList<QuickOption> _comms = Array.Empty<QuickOption>();
    private IReadOnlyList<QuickOption> _displays = Array.Empty<QuickOption>();
    private VolumeInfo? _outVolume;
    private VolumeInfo? _inVolume;

    // Device cache the lists above come from (null while QuickSwitch has not read the devices yet).
    private QuickSnapshot? _appliedSnapshot;

    // Opening time checkpoints (ms of _openClock), logged once the overlay is shown.
    private long _windowsDoneAt;
    private long _dataDoneAt;
    private long _layoutDoneAt;

    // Main view: selected row, which of the two mute buttons is selected (0 = sound, 1 = microphone) and which of the
    // three power buttons (index into PowerActions).
    private int _selected;
    private int _toggleIndex;
    private int _powerIndex;

    // Header: battery of the controller that opened the overlay (null when opened otherwise or unknown) and the time.
    private readonly int? _controllerIndex;
    private readonly ButtonIconFamily _buttonIcons;
    private ControllerBattery? _battery;
    private int _ticks;
    private string _clockText = CurrentTime();

    // Immersive mode is read and changed through the app (null in the warm-up overlay: no row).
    private readonly Func<bool>? _immersiveEnabled;
    private readonly Action<bool>? _setImmersive;

    // Vista de lista (en el centro de la pantalla): qué se elige, opciones y opción marcada.
    private PickerKind? _picker;
    private IReadOnlyList<QuickOption> _options = Array.Empty<QuickOption>();
    private int _optionSelected;

    // Disposición calculada por Relayout. Los elementos de la lista usan coordenadas de contenido: se dibujan
    // desplazados _scroll píxeles dentro de _viewport.
    private readonly List<Rectangle> _itemRects = new();
    private Rectangle _viewport;
    private int _scroll;
    private int _scrollMax;
    private Rectangle _titleRect;
    private Rectangle _subtitleRect;
    private Rectangle _statusRect;
    private Rectangle _hintRect;

    /// <param name="inputHeld">Si el mando tiene algo pulsado: el overlay no devuelve el foco hasta que se suelta.</param>
    /// <param name="minimizedGame">
    /// Juego en pantalla completa exclusiva que se ha minimizado para poder enseñar el overlay: es la ventana «de antes»
    /// y, al cerrar el overlay, se restaura (salvo que se haya cerrado el juego).
    /// </param>
    /// <param name="controllerIndex">XInput index of the controller that opened the overlay, to show its battery.</param>
    /// <param name="immersiveEnabled">Whether the immersive console mode is on.</param>
    /// <param name="setImmersive">Turns the immersive console mode on or off (and saves it).</param>
    /// <param name="buttonIcons">Console whose button icons the hints at the bottom show.</param>
    public OverlayForm(Icon icon, ModeSwitcher switcher, Func<bool> inputHeld, IntPtr minimizedGame = default,
        int? controllerIndex = null, Func<bool>? immersiveEnabled = null, Action<bool>? setImmersive = null,
        ButtonIconFamily buttonIcons = ButtonIconFamily.Xbox)
    {
        _buttonIcons = buttonIcons;
        _controllerIndex = controllerIndex;
        _immersiveEnabled = immersiveEnabled;
        _setImmersive = setImmersive;
        _minimizedGame = minimizedGame;
        _previousForeground = minimizedGame != IntPtr.Zero ? minimizedGame : WindowFocus.Foreground;
        _previousProcess = WindowFocus.ProcessNameOf(_previousForeground);
        if (minimizedGame == IntPtr.Zero && switcher.State.Mode == AppMode.Console)
        {
            // Si lo que está delante no es ni el juego ni el frontal, se ofrece volver al que haya debajo.
            var isGame = GameProcesses.Checker();
            if (!GameProcesses.IsGameWindow(_previousForeground, isGame))
            {
                _returnTarget = GameProcesses.FindGameWindow(isGame);
            }
        }
        _game = GameWindow.FromWindow(_returnTarget != IntPtr.Zero ? _returnTarget : _previousForeground);
        _switcher = switcher;
        _inputHeld = inputHeld;
        _windowsDoneAt = _openClock.ElapsedMilliseconds;

        Text = "Console Mode";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Background;
        KeyPreview = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _idleTimer = new System.Windows.Forms.Timer { Interval = (int)IdleClose.TotalMilliseconds };
        _idleTimer.Tick += (_, _) => CloseWhenReleased();
        _releaseTimer.Tick += (_, _) => CheckReleased();
        _focusTimer.Tick += (_, _) => EnsureFocus();
        _volumeTimer.Tick += (_, _) => OnTick();

        // Devices come from QuickSwitch's cache: asking Windows for them here took hundreds of milliseconds.
        CopySnapshot(QuickSwitch.Snapshot);
        _dataDoneAt = _openClock.ElapsedMilliseconds;

        // Se calcula con el escalado de la pantalla de destino y se coloca ya en su esquina antes de mostrarse.
        _dpi = DpiOf(TargetArea());
        Relayout();
        Place();
        _layoutDoneAt = _openClock.ElapsedMilliseconds;
    }

    /// <summary>
    /// Crea y dibuja un overlay sin mostrarlo, para que la primera vez que se abra de verdad no tarde: si tarda, Big
    /// Picture sigue con el foco mientras tanto y recibe las mismas pulsaciones que luego mueven el overlay.
    /// </summary>
    public static void Warmup(Icon icon, ModeSwitcher switcher)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var form = new OverlayForm(icon, switcher, () => false);
            form.CreateControl();
            using var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            Log.Info($"Overlay precargado en {clock.ElapsedMilliseconds} ms");
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo precargar el overlay", ex);
        }
    }

    /// <summary>Acción que llega del mando (XInput).</summary>
    public void HandlePadAction(PadAction action)
    {
        _lastPadActionAt = Environment.TickCount64;
        HandleAction(action);
    }

    /// <summary>Acción del mando, del teclado o del ratón mientras el overlay está abierto.</summary>
    private void HandleAction(PadAction action)
    {
        if (_busy || _closing)
        {
            return;
        }
        _idleTimer.Stop();
        _idleTimer.Start();

        if (Confirming)
        {
            switch (action)
            {
                case PadAction.Up:
                case PadAction.Left:
                    SelectConfirm(_confirmSelected - 1);
                    break;
                case PadAction.Down:
                case PadAction.Right:
                    SelectConfirm(_confirmSelected + 1);
                    break;
                case PadAction.Accept:
                    AnswerConfirm();
                    break;
                case PadAction.Back:
                    CloseConfirm();
                    break;
                case PadAction.ToggleOverlay:
                    CloseWhenReleased();
                    break;
            }
            return;
        }

        if (_picker != null)
        {
            switch (action)
            {
                case PadAction.Up:
                    SelectOption(_optionSelected - 1);
                    break;
                case PadAction.Down:
                    SelectOption(_optionSelected + 1);
                    break;
                case PadAction.Accept:
                    _ = ApplyOptionAsync();
                    break;
                case PadAction.Back:
                    ClosePicker();
                    break;
                case PadAction.ToggleOverlay:
                    CloseWhenReleased();
                    break;
            }
            return;
        }

        switch (action)
        {
            case PadAction.Up:
                SelectRow(_selected - 1);
                break;
            case PadAction.Down:
                SelectRow(_selected + 1);
                break;
            case PadAction.Left:
            case PadAction.Right:
                Adjust(action == PadAction.Left ? -1 : 1);
                break;
            case PadAction.Accept:
                ActivateRow();
                break;
            case PadAction.Back:
            case PadAction.ToggleOverlay:
                CloseWhenReleased();
                break;
        }
    }

    // ------------------------------------------------------------------
    // Ciclo de vida de la ventana
    // ------------------------------------------------------------------

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Por si Windows ha creado la ventana en otra pantalla que la calculada.
        if (DeviceDpi != _dpi)
        {
            _dpi = DeviceDpi;
            Relayout();
        }
        var showStartedAt = _openClock.ElapsedMilliseconds;
        Place();
        GrabFocus();
        Log.Info($"Overlay: abierto en {_openClock.ElapsedMilliseconds} ms");
        var openedAt = _openClock.ElapsedMilliseconds;
        Log.Info($"Overlay timing: windows {_windowsDoneAt} ms, data {_dataDoneAt - _windowsDoneAt} ms, " +
                 $"layout {_layoutDoneAt - _dataDoneAt} ms, show {showStartedAt - _layoutDoneAt} ms, " +
                 $"place+focus {openedAt - showStartedAt} ms, cache {(_appliedSnapshot != null ? "ready" : "loading")}");
        _idleTimer.Start();
        _volumeTimer.Start();
        _ = RefreshBatteryAsync();

        // A read that finished since the constructor, and a fresh one (volumes, retry of a list that failed).
        ApplyLatestSnapshot();
        QuickSwitch.RequestRefresh();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DPICHANGED)
        {
            // Nada de reescalado automático: se vuelve a calcular todo con el nuevo DPI.
            var newDpi = (int)((long)m.WParam & 0xFFFF);
            if (newDpi > 0 && newDpi != _dpi)
            {
                _dpi = newDpi;
                Relayout();
                Place();
            }
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Esquinas redondeadas en Windows 11 (en Windows 10 se ignora).
        var round = DWMWCP_ROUND;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Si Steam traduce el mando a teclas (configuración de escritorio de Steam Input), la misma pulsación llega
        // dos veces: por XInput y como tecla. Mientras se use el mando, se ignoran las teclas.
        if (Environment.TickCount64 - _lastPadActionAt < 500)
        {
            e.Handled = true;
            base.OnKeyDown(e);
            return;
        }

        PadAction? action = e.KeyCode switch
        {
            Keys.Up => PadAction.Up,
            Keys.Down => PadAction.Down,
            Keys.Left => PadAction.Left,
            Keys.Right => PadAction.Right,
            Keys.Enter or Keys.Space => PadAction.Accept,
            Keys.Escape or Keys.Back => PadAction.Back,
            _ => null,
        };
        if (action != null)
        {
            HandleAction(action.Value);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (!_viewport.Contains(e.Location))
        {
            return;
        }
        var index = _itemRects.FindIndex(r => OnScreen(r).Contains(e.Location));
        if (index < 0)
        {
            return;
        }

        if (Confirming)
        {
            SelectConfirm(index);
            HandleAction(PadAction.Accept);
            return;
        }
        if (_picker != null)
        {
            SelectOption(index);
            HandleAction(PadAction.Accept);
            return;
        }

        SelectRow(index);
        var row = _rows[index];
        var bounds = OnScreen(_itemRects[index]);
        if (row.Kind == RowKind.MuteToggles)
        {
            _toggleIndex = e.X < bounds.Left + bounds.Width / 2 ? 0 : 1;
        }
        else if (row.Kind == RowKind.PowerActions)
        {
            _powerIndex = Math.Clamp((e.X - bounds.Left) * PowerActions.Length / Math.Max(1, bounds.Width), 0, PowerActions.Length - 1);
        }
        else if (row.Kind == RowKind.Volume && VolumeTrack(bounds) is { } track && e.X >= track.Left - Px(8))
        {
            // Clic en la barra: el volumen pasa a ese punto.
            var percent = (int)Math.Round(Math.Clamp((e.X - track.Left) / (double)track.Width, 0, 1) * 100);
            SetVolume(row.Flow, percent);
            return;
        }
        HandleAction(PadAction.Accept);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // Si el usuario hace clic con el ratón en otro sitio, el overlay se va. Si el foco se lo ha quitado
        // Big Picture, el vigilante de foco lo recupera.
        if (!_busy && MouseButtons != MouseButtons.None)
        {
            BeginInvoke(Close);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _idleTimer.Dispose();
        _focusTimer.Dispose();
        _releaseTimer.Dispose();
        _volumeTimer.Dispose();
        _switcher.StateChanged -= OnSwitcherStateChanged;
        _foregroundWatcher?.Dispose();
        _foregroundWatcher = null;
        // Solo devolvemos el foco si seguía en el overlay (si el usuario hizo clic en otra ventana, la respetamos). Si se
        // ha cerrado el juego, va al frontal (Big Picture, Playnite...) para seguir con el mando.
        if (_returnRequested && !_gameClosed)
        {
            WindowFocus.Activate(_returnTarget);
        }
        else if (_minimizedGame != IntPtr.Zero && !_gameClosed)
        {
            // Se había minimizado el juego para enseñar el overlay: vuelve a pantalla completa.
            WindowFocus.RestoreMinimized(_minimizedGame);
        }
        else if (WindowFocus.Foreground == Handle && !(_gameClosed && ConsoleFrontend.Activate()))
        {
            WindowFocus.Restore(_previousForeground);
        }
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _idleTimer.Dispose();
            _focusTimer.Dispose();
            _releaseTimer.Dispose();
            _volumeTimer.Dispose();
            _fonts?.Dispose();
            _fonts = null;
        }
    }

    // ------------------------------------------------------------------
    // Datos
    // ------------------------------------------------------------------

    /// <summary>Takes the lists from the cache (null: not read yet, the rows say «Cargando…») and rebuilds the rows.</summary>
    private void CopySnapshot(QuickSnapshot? snapshot)
    {
        var data = snapshot ?? QuickSnapshot.Empty;
        _audio = data.Audio;
        _mics = data.Microphones;
        _comms = data.Communications;
        _displays = data.DisplayOptions;
        _outVolume = data.OutputVolume;
        _inVolume = data.InputVolume;
        _appliedSnapshot = snapshot;
        BuildRows();
    }

    /// <summary>Copies the cache if it is newer than what the rows show. Returns whether it did.</summary>
    private bool CopyLatestSnapshot()
    {
        var latest = QuickSwitch.Snapshot;
        if (latest == null || ReferenceEquals(latest, _appliedSnapshot))
        {
            return false;
        }
        CopySnapshot(latest);
        return true;
    }

    private void BuildRows()
    {
        var selectedKey = _selected < _rows.Count ? _rows[_selected].Key : null;
        _rows.Clear();

        string Current(IReadOnlyList<QuickOption> options) => _appliedSnapshot == null
            ? "Cargando…"
            : options.FirstOrDefault(o => o.IsCurrent)?.Name ?? (options.Count == 0 ? "No hay ninguno" : "Sin elegir");

        var state = _switcher.State;
        if (!state.Busy)
        {
            _rows.Add(state.Mode == AppMode.Console
                ? new Row(RowKind.SwitchMode, "mode", "Volver a modo PC",
                    ConsoleFrontend.LaunchesOnConsoleMode ? $"Cierra {ConsoleFrontend.Name} y aplica el modo PC" : "Aplica el modo PC", GlyphPc, PcColor,
                    Target: AppMode.Pc)
                : new Row(RowKind.SwitchMode, "mode", "Ir a modo consola",
                    ConsoleFrontend.LaunchesOnConsoleMode ? $"Abre {ConsoleFrontend.Name} en la pantalla de la consola" : "Aplica el modo consola", GlyphGame,
                    ConsoleColor, Target: AppMode.Console));
        }
        if (_returnTarget != IntPtr.Zero && !_gameClosed)
        {
            _rows.Add(new Row(RowKind.ReturnToGame, "return", "Volver al juego",
                _game?.Name ?? ConsoleFrontend.Name, GlyphPlay, ConsoleColor));
        }
        if (_game != null && !_gameClosed)
        {
            _rows.Add(new Row(RowKind.CloseGame, "close-game", _offerForceClose ? "Forzar cierre del juego" : "Cerrar juego",
                _game.Name, GlyphClose, CloseGameColor));
        }
        if (_outVolume != null || _inVolume != null)
        {
            _rows.Add(new Row(RowKind.MuteToggles, "mute", "", "", GlyphMute, MutedOnColor));
        }
        _rows.Add(new Row(RowKind.Picker, "audio", "Salida de audio", Current(_audio), GlyphVolume, AudioColor, PickerKind.Audio));
        if (_outVolume != null)
        {
            _rows.Add(new Row(RowKind.Volume, "volume-out", "Volumen", "", GlyphVolume, AudioColor, Flow: AudioFlow.Output));
        }
        if (_mics.Count > 0)
        {
            _rows.Add(new Row(RowKind.Picker, "mic", "Micrófono", Current(_mics), GlyphMic, MicColor, PickerKind.Microphone));
        }
        if (_inVolume != null)
        {
            _rows.Add(new Row(RowKind.Volume, "volume-in", "Volumen del micrófono", "", GlyphMic, MicColor, Flow: AudioFlow.Input));
        }
        if (_comms.Count > 0)
        {
            _rows.Add(new Row(RowKind.Picker, "comms", "Comunicaciones (Discord, Teams…)", Current(_comms), GlyphHeadset, CommsColor,
                PickerKind.Communications));
        }
        _rows.Add(new Row(RowKind.Picker, "display", "Pantalla principal", Current(_displays), GlyphDisplay, DisplayColor,
            PickerKind.Display));
        if (state.Mode == AppMode.Console && !state.Busy && _immersiveEnabled != null && _setImmersive != null)
        {
            var immersive = _immersiveEnabled();
            _rows.Add(new Row(RowKind.ImmersiveToggle, "immersive", "Modo inmersivo",
                immersive ? "Activado: solo se ven el frontal y los juegos" : "Desactivado: se ven todas las ventanas",
                GlyphImmersive, immersive ? ConsoleColor : MutedOffColor));
        }
        // Last, far from the row the overlay starts on: it always asks first, but it is better kept out of reach.
        _rows.Add(new Row(RowKind.PowerActions, "power", "", "", GlyphPower, CloseGameColor));

        var index = _rows.FindIndex(r => r.Key == selectedKey);
        if (index < 0)
        {
            // Se empieza en «Volver al juego» si se ofrece (seguramente se ha abierto el overlay para eso) o en la salida
            // de audio (nunca en el cambio de modo, para que un A sin querer no cambie de modo).
            index = _rows.FindIndex(r => r.Key == "return");
            if (index < 0)
            {
                index = Math.Max(0, _rows.FindIndex(r => r.Key == "audio"));
            }
        }
        _selected = Math.Clamp(index, 0, Math.Max(0, _rows.Count - 1));
    }

    /// <summary>
    /// QuickSwitch has read the devices again (one was connected or removed, or the first read has finished). With a list
    /// or the dialog open, or while a change is applied, it waits: it is applied when going back to the main view.
    /// </summary>
    public void ApplyLatestSnapshot()
    {
        if (IsDisposed || _picker != null || Confirming || _busy || _closing)
        {
            return;
        }
        var latest = QuickSwitch.Snapshot;
        if (latest == null || ReferenceEquals(latest, _appliedSnapshot))
        {
            return;
        }
        if (_appliedSnapshot != null && latest.HasSameDevices(_appliedSnapshot) &&
            (latest.OutputVolume == null) == (_outVolume == null) && (latest.InputVolume == null) == (_inVolume == null))
        {
            // Only the volumes changed: same rows, same size.
            _appliedSnapshot = latest;
            _outVolume = latest.OutputVolume;
            _inVolume = latest.InputVolume;
            Invalidate();
            return;
        }
        CopySnapshot(latest);
        // The target display (and its scale) may only be known now, e.g. when the overlay opened before the first read.
        _dpi = DpiOf(TargetArea());
        Relayout();
        Place();
    }

    /// <summary>El volumen también se puede cambiar con el teclado o desde Windows: se vuelve a leer cada segundo.</summary>
    private void RefreshVolumes()
    {
        if (_picker != null || Confirming || _busy || _closing)
        {
            return;
        }
        var outVolume = _outVolume != null ? QuickSwitch.Volume(AudioFlow.Output) : null;
        var inVolume = _inVolume != null ? QuickSwitch.Volume(AudioFlow.Input) : null;
        if (outVolume != _outVolume || inVolume != _inVolume)
        {
            _outVolume = outVolume ?? _outVolume;
            _inVolume = inVolume ?? _inVolume;
            Invalidate();
        }
    }

    /// <summary>Every second: the clock, the controller battery (every BatteryRefreshTicks) and the volume.</summary>
    private void OnTick()
    {
        var clock = CurrentTime();
        if (clock != _clockText)
        {
            _clockText = clock;
            Invalidate(_subtitleRect);
        }
        if (++_ticks % BatteryRefreshTicks == 0)
        {
            _ = RefreshBatteryAsync();
        }
        RefreshVolumes();
    }

    private static string CurrentTime() => DateTime.Now.ToString("t", System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>Reads the controller battery off the UI thread (XInput can be slow with wireless controllers).</summary>
    private async Task RefreshBatteryAsync()
    {
        if (_controllerIndex is not { } index)
        {
            return;
        }
        var battery = await Task.Run(() => GamepadWatcher.ReadBattery(index));
        if (IsDisposed || battery == _battery)
        {
            return;
        }
        _battery = battery;
        Invalidate();
    }

    private void SelectRow(int index)
    {
        if (_rows.Count == 0)
        {
            return;
        }
        _selected = Math.Clamp(index, 0, _rows.Count - 1);
        ScrollToSelected();
        Invalidate();
    }

    private void SelectOption(int index)
    {
        if (_options.Count == 0)
        {
            return;
        }
        _optionSelected = Math.Clamp(index, 0, _options.Count - 1);
        ScrollToSelected();
        Invalidate();
    }

    private void SelectConfirm(int index)
    {
        _confirmSelected = Math.Clamp(index, 0, _confirmRows.Count - 1);
        Invalidate();
    }

    private bool Confirming => _confirm != null;

    private int SelectedItem => Confirming ? _confirmSelected : _picker != null ? _optionSelected : _selected;

    /// <summary>El diálogo de confirmación y las listas van en el centro de la pantalla; la vista principal, en la esquina.</summary>
    private bool Centered => _picker != null || Confirming;

    /// <summary>Desplaza la lista para que el elemento elegido se vea.</summary>
    private void ScrollToSelected()
    {
        var index = SelectedItem;
        if (index < 0 || index >= _itemRects.Count || _viewport.Height <= 0)
        {
            return;
        }

        var item = _itemRects[index];
        if (item.Top - _viewport.Top < _scroll)
        {
            _scroll = item.Top - _viewport.Top;
        }
        else if (item.Bottom - _viewport.Top > _scroll + _viewport.Height)
        {
            _scroll = item.Bottom - _viewport.Top - _viewport.Height;
        }
        _scroll = Math.Clamp(_scroll, 0, _scrollMax);
    }

    private Rectangle OnScreen(Rectangle item) => item with { Y = item.Y - _scroll };

    private void SetStatus(string text, bool error = false)
    {
        var sizeChanged = (_status.Length == 0) != (text.Length == 0);
        _status = text;
        _statusIsError = error;
        if (sizeChanged)
        {
            Relayout();
            Place();
        }
        Invalidate();
    }

    // ------------------------------------------------------------------
    // Acciones
    // ------------------------------------------------------------------

    /// <summary>Izquierda / derecha: volumen en las barras y botón en la fila de silenciar.</summary>
    private void Adjust(int direction)
    {
        if (_selected >= _rows.Count)
        {
            return;
        }
        var row = _rows[_selected];
        if (row.Kind == RowKind.MuteToggles)
        {
            _toggleIndex = Math.Clamp(_toggleIndex + direction, 0, 1);
            Invalidate();
        }
        else if (row.Kind == RowKind.PowerActions)
        {
            _powerIndex = Math.Clamp(_powerIndex + direction, 0, PowerActions.Length - 1);
            Invalidate();
        }
        else if (row.Kind == RowKind.Volume && VolumeOf(row.Flow) is { } volume)
        {
            SetVolume(row.Flow, volume.Percent + direction * VolumeStep);
        }
    }

    private VolumeInfo? VolumeOf(AudioFlow flow) => flow == AudioFlow.Input ? _inVolume : _outVolume;

    private void SetVolume(AudioFlow flow, int percent)
    {
        percent = Math.Clamp((int)Math.Round(percent / (double)VolumeStep) * VolumeStep, 0, 100);
        try
        {
            QuickSwitch.SetVolume(flow, percent);
            var muted = VolumeOf(flow)?.Muted ?? false;
            if (flow == AudioFlow.Input)
            {
                _inVolume = new VolumeInfo(percent, muted);
            }
            else
            {
                _outVolume = new VolumeInfo(percent, muted);
            }
            Invalidate();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
            Log.Error("Overlay: no se pudo cambiar el volumen", ex);
        }
    }

    private void ToggleMute(AudioFlow flow)
    {
        if (VolumeOf(flow) is not { } volume)
        {
            return;
        }
        try
        {
            QuickSwitch.SetMuted(flow, !volume.Muted);
            var updated = volume with { Muted = !volume.Muted };
            if (flow == AudioFlow.Input)
            {
                _inVolume = updated;
                SetStatus(updated.Muted ? "Micrófono silenciado" : "Micrófono activado");
            }
            else
            {
                _outVolume = updated;
                SetStatus(updated.Muted ? "Sonido silenciado" : "Sonido activado");
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
            Log.Error("Overlay: no se pudo silenciar", ex);
        }
    }

    private void ActivateRow()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var row = _rows[_selected];
        switch (row.Kind)
        {
            case RowKind.MuteToggles:
                ToggleMute(_toggleIndex == 0 ? AudioFlow.Output : AudioFlow.Input);
                return;
            case RowKind.Volume:
                ToggleMute(row.Flow);
                return;
            case RowKind.Picker:
                OpenPicker(row.Picker);
                return;
            case RowKind.CloseGame:
                OpenConfirm(ConfirmKind.CloseGame);
                return;
            case RowKind.ReturnToGame:
                ReturnToGame();
                return;
            case RowKind.ImmersiveToggle:
                ToggleImmersive();
                return;
            case RowKind.PowerActions:
                OpenConfirm(PowerActions[_powerIndex]);
                return;
        }

        if (row.Target == AppMode.Console)
        {
            // Primero se cierra el overlay (esperando a que se suelte la A) y se devuelve el foco; después se cambia
            // de modo, para que Big Picture se abra delante sin pelear por el foco con el overlay.
            Log.Info("Overlay: ir a modo consola");
            _busy = true;
            _idleTimer.Stop();
            SetStatus("Pasando a modo consola…");
            FormClosed += (_, _) => _ = _switcher.SwitchAsync(AppMode.Console);
            CloseWhenReleased();
            return;
        }

        // El overlay se queda delante (con el foco) hasta que Big Picture se ha cerrado. Si se cerrase antes,
        // Big Picture recuperaría el foco con la A aún pulsada y lanzaría el juego que tuviera seleccionado.
        Log.Info("Overlay: volver a modo PC");
        _busy = true;
        _switchingToPc = true;
        _idleTimer.Stop();
        SetStatus("Volviendo a modo PC…");
        _switcher.StateChanged += OnSwitcherStateChanged;
        _ = _switcher.SwitchAsync(AppMode.Pc);
    }

    /// <summary>
    /// Vuelve al juego (o al frontal) que había debajo: la ventana que se había puesto delante se minimiza para que no lo
    /// vuelva a tapar (el escritorio y la barra de tareas no se tocan) y, al cerrarse el overlay, el foco va al juego.
    /// </summary>
    private void ReturnToGame()
    {
        Log.Info($"Overlay: volver a {WindowFocus.Describe(_returnTarget)}; delante estaba {WindowFocus.Describe(_previousForeground)}");
        var intruder = _previousForeground;
        if (intruder != IntPtr.Zero && !string.Equals(WindowFocus.ProcessNameOf(intruder), "explorer", StringComparison.OrdinalIgnoreCase))
        {
            GetWindowThreadProcessId(intruder, out var pid);
            if (pid != Environment.ProcessId)
            {
                WindowFocus.Minimize(intruder);
            }
        }
        _returnRequested = true;
        CloseWhenReleased();
    }

    /// <summary>
    /// Asks in the middle of the screen whether to close (or force close) the game, or to suspend, restart or shut down
    /// the PC.
    /// </summary>
    private void OpenConfirm(ConfirmKind kind)
    {
        if (kind == ConfirmKind.CloseGame && _game == null)
        {
            return;
        }
        var (_, _, glyph, color) = ConfirmInfo(kind);
        _confirmRows.Clear();
        _confirmRows.Add(kind switch
        {
            ConfirmKind.CloseGame when _offerForceClose =>
                new Row(RowKind.CloseGame, "confirm-yes", "Sí, forzar el cierre", "Se termina al momento", glyph, color),
            ConfirmKind.CloseGame => new Row(RowKind.CloseGame, "confirm-yes", "Sí, cerrar el juego", "Como Alt+F4", glyph, color),
            ConfirmKind.Sleep => new Row(RowKind.CloseGame, "confirm-yes", "Sí, suspender", "El PC se queda en reposo", glyph, color),
            ConfirmKind.Restart => new Row(RowKind.CloseGame, "confirm-yes", "Sí, reiniciar", "Se reinicia al momento", glyph, color),
            _ => new Row(RowKind.CloseGame, "confirm-yes", "Sí, apagar", "Se apaga al momento", glyph, color),
        });
        _confirmRows.Add(new Row(RowKind.SwitchMode, "confirm-no",
            kind == ConfirmKind.CloseGame ? "No, seguir jugando" : "No, volver", "Vuelve al overlay", GlyphBack, MutedOffColor));
        _confirmSelected = 1; // «No»: an extra A neither closes the game nor shuts the PC down
        _confirm = kind;
        _scroll = 0;
        _status = "";
        Relayout();
        Place();
    }

    private void CloseConfirm()
    {
        _confirm = null;
        _scroll = 0;
        CopyLatestSnapshot();
        Relayout();
        Place();
        GrabFocus();
    }

    private void AnswerConfirm()
    {
        var kind = _confirm;
        var yes = _confirmSelected == 0;
        CloseConfirm();
        if (!yes || kind == null)
        {
            return;
        }
        if (kind == ConfirmKind.CloseGame)
        {
            _ = CloseGameAsync();
        }
        else
        {
            _ = RunPowerActionAsync(kind.Value);
        }
    }

    /// <summary>Suspends, restarts or shuts down the PC (already confirmed).</summary>
    private async Task RunPowerActionAsync(ConfirmKind kind)
    {
        Log.Info($"Overlay: power action {kind}");
        _busy = true;
        _idleTimer.Stop();
        SetStatus(kind switch
        {
            ConfirmKind.Sleep => "Suspendiendo el PC…",
            ConfirmKind.Restart => "Reiniciando el PC…",
            _ => "Apagando el PC…",
        });
        Action action = kind switch
        {
            ConfirmKind.Sleep => PowerControl.Sleep,
            ConfirmKind.Restart => PowerControl.Restart,
            _ => PowerControl.Shutdown,
        };
        try
        {
            // Suspending only returns once the PC wakes up again.
            await Task.Run(action);
            if (kind != ConfirmKind.Sleep)
            {
                await Task.Delay(PowerActionTimeout);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Overlay: power action {kind} failed", ex);
            _busy = false;
            if (!IsDisposed)
            {
                SetStatus(ex.Message, error: true);
                _idleTimer.Start();
                ApplyLatestSnapshot();
            }
            return;
        }

        _busy = false;
        if (IsDisposed)
        {
            return;
        }
        if (kind == ConfirmKind.Sleep)
        {
            // The PC has woken up: the overlay is no longer needed.
            CloseWhenReleased();
            return;
        }
        SetStatus("Windows aún no lo ha hecho: puede que algún programa lo esté impidiendo.", error: true);
        _idleTimer.Start();
        ApplyLatestSnapshot();
    }

    /// <summary>Turns the immersive console mode on or off (saved, like the window checkbox).</summary>
    private void ToggleImmersive()
    {
        if (_immersiveEnabled == null || _setImmersive == null)
        {
            return;
        }
        try
        {
            var enabled = !_immersiveEnabled();
            _setImmersive(enabled);
            Log.Info($"Overlay: immersive mode {(enabled ? "on" : "off")}");
            BuildRows();
            SetStatus(enabled ? "Modo inmersivo activado" : "Modo inmersivo desactivado");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
            Log.Error("Overlay: could not switch the immersive mode", ex);
        }
    }

    /// <summary>
    /// Cierra el juego que estaba delante (ya confirmado) como Alt+F4. Si no se cierra en unos segundos (hay juegos que
    /// no hacen caso, o preguntan), se ofrece forzar el cierre.
    /// </summary>
    private async Task CloseGameAsync()
    {
        if (_game is not { } game)
        {
            return;
        }

        _busy = true;
        _idleTimer.Stop();
        try
        {
            if (_offerForceClose)
            {
                SetStatus($"Forzando el cierre de {game.Name}…");
                await Task.Run(game.Kill);
            }
            else
            {
                SetStatus($"Cerrando {game.Name}…");
                game.RequestClose();
            }

            if (await game.WaitForExitAsync(TimeSpan.FromSeconds(6)))
            {
                Log.Info($"Overlay: juego «{game.Name}» cerrado");
                _gameClosed = true;
                _busy = false;
                if (!IsDisposed)
                {
                    CloseWhenReleased(); // al cerrarse el overlay, el foco vuelve al frontal
                }
                return;
            }

            _offerForceClose = true;
            SetStatus($"{game.Name} no se ha cerrado. Elige «Forzar cierre del juego».", error: true);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
            Log.Error("Overlay: no se pudo cerrar el juego", ex);
        }

        _busy = false;
        if (!IsDisposed)
        {
            _idleTimer.Start();
            BuildRows();
            Invalidate();
            ApplyLatestSnapshot();
        }
    }

    private void OpenPicker(PickerKind picker)
    {
        _options = picker switch
        {
            PickerKind.Audio => _audio,
            PickerKind.Microphone => _mics,
            PickerKind.Communications => _comms,
            _ => _displays,
        };
        if (_options.Count == 0)
        {
            return;
        }
        _picker = picker;
        _optionSelected = Math.Max(0, _options.ToList().FindIndex(o => o.IsCurrent));
        _scroll = 0;
        _status = "";
        Relayout();
        Place();
    }

    private void ClosePicker()
    {
        _picker = null;
        _scroll = 0;
        CopyLatestSnapshot();
        Relayout();
        Place();
        GrabFocus();
    }

    private async Task ApplyOptionAsync()
    {
        if (_picker is not { } picker || _options.Count == 0)
        {
            return;
        }
        var option = _options[_optionSelected];
        if (option.IsCurrent)
        {
            ClosePicker();
            return;
        }

        _busy = true;
        try
        {
            await Task.Run(() =>
            {
                switch (picker)
                {
                    case PickerKind.Audio:
                        QuickSwitch.SetAudio(option.Id, "Overlay");
                        break;
                    case PickerKind.Microphone:
                        QuickSwitch.SetMicrophone(option.Id, "Overlay");
                        break;
                    case PickerKind.Communications:
                        QuickSwitch.SetCommunications(option.Id, "Overlay");
                        break;
                    default:
                        QuickSwitch.SetDisplay(option.Id, "Overlay");
                        break;
                }
            });
            // The switch has asked for a new read (off the UI thread): the lists show the change on the main view.
            var fresh = await QuickSwitch.PendingRefreshAsync();
            if (picker == PickerKind.Display)
            {
                _targetDisplayGdiName = fresh.FindDisplay(option.Id)?.GdiName;
            }
            _status = picker switch
            {
                PickerKind.Audio => $"Audio: {option.Name}",
                PickerKind.Microphone => $"Micrófono: {option.Name}",
                PickerKind.Communications => $"Comunicaciones: {option.Name}",
                _ => $"Pantalla principal: {option.Name}",
            };
            _statusIsError = false;
        }
        catch (Exception ex)
        {
            _status = ex.Message;
            _statusIsError = true;
            Log.Error("Overlay: no se pudo aplicar el cambio", ex);
        }
        finally
        {
            _busy = false;
        }

        if (IsDisposed)
        {
            return;
        }

        // Si la pantalla elegida tiene otro escalado, se recalcula con el suyo.
        _dpi = DpiOf(TargetArea());
        ClosePicker();
    }

    // ------------------------------------------------------------------
    // Disposición
    // ------------------------------------------------------------------

    /// <summary>
    /// Calcula la posición de cada elemento y el tamaño de la ventana con el DPI actual. Si la lista no cabe en la
    /// pantalla, se queda con la altura disponible y se desplaza siguiendo al elemento elegido.
    /// </summary>
    private void Relayout()
    {
        _fonts?.Dispose();
        _fonts = new Fonts(_dpi);
        var f = _fonts;
        _itemRects.Clear();

        var padX = Px(22);
        var padY = Px(20);
        var gap = Px(8);

        int contentWidth;
        int itemHeight;
        int count;
        var subtitleLines = 1;
        if (Confirming)
        {
            contentWidth = Px(520);
            itemHeight = Math.Max(Px(62), f.Small.Height + f.Row.Height + Px(20));
            count = _confirmRows.Count;
            subtitleLines = 2;
        }
        else if (_picker == null)
        {
            contentWidth = Px(470);
            itemHeight = Math.Max(Px(62), f.Small.Height + f.Row.Height + Px(20));
            count = _rows.Count;
        }
        else
        {
            var widest = _options.Select(o => TextRenderer.MeasureText(o.Name, f.Row, Size.Empty, TextFlags).Width)
                .DefaultIfEmpty(0).Max() + Px(110);
            contentWidth = Math.Clamp(widest, Px(440), Px(760));
            itemHeight = Math.Max(Px(52), f.Row.Height + Px(24));
            count = _options.Count;
        }

        var y = padY;
        _titleRect = new Rectangle(padX, y, contentWidth, f.Title.Height + Px(4));
        y = _titleRect.Bottom;
        _subtitleRect = new Rectangle(padX, y + (subtitleLines > 1 ? Px(6) : 0), contentWidth, f.Small.Height * subtitleLines + Px(2));
        y = _subtitleRect.Bottom + Px(14);

        var listTop = y;
        for (var i = 0; i < count; i++)
        {
            _itemRects.Add(new Rectangle(padX, y, contentWidth, itemHeight));
            y += itemHeight + gap;
        }
        var listHeight = Math.Max(0, y - gap - listTop);

        var footerHeight = Px(16) + (_status.Length > 0 ? f.Small.Height + Px(8) : 0) + Px(24);
        var maxClientHeight = TargetArea().Height - 2 * Px(40);
        var viewportHeight = Math.Max(itemHeight * 2, Math.Min(listHeight, maxClientHeight - listTop - footerHeight - padY));
        _viewport = new Rectangle(0, listTop, contentWidth + padX * 2, viewportHeight);
        _scrollMax = Math.Max(0, listHeight - viewportHeight);

        y = listTop + viewportHeight + Px(16);
        if (_status.Length > 0)
        {
            _statusRect = new Rectangle(padX, y, contentWidth, f.Small.Height);
            y = _statusRect.Bottom + Px(8);
        }
        else
        {
            _statusRect = Rectangle.Empty;
        }
        _hintRect = new Rectangle(padX, y, contentWidth, Px(24));
        y = _hintRect.Bottom;

        ClientSize = new Size(contentWidth + padX * 2, y + padY);
        ScrollToSelected();
        Invalidate();
    }

    /// <summary>Barra de volumen dentro de una fila (en coordenadas de pantalla).</summary>
    private Rectangle? VolumeTrack(Rectangle row)
    {
        if (_fonts == null)
        {
            return null;
        }
        var text = TextArea(row);
        var top = row.Top + Px(10) + _fonts.Small.Height + Px(10);
        return new Rectangle(text.Left, top, text.Width - Px(4), Px(8));
    }

    /// <summary>Zona de texto de una fila, a la derecha del icono.</summary>
    private Rectangle TextArea(Rectangle row)
    {
        var left = row.Left + Px(14) + Px(38) + Px(14);
        return Rectangle.FromLTRB(left, row.Top, row.Right - Px(16), row.Bottom);
    }

    // ------------------------------------------------------------------
    // Dibujo
    // ------------------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Background);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var border = new Pen(Border, Math.Max(1, Px(1))))
        {
            g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
        if (_fonts == null)
        {
            return;
        }
        var f = _fonts;

        // ----- Cabecera -----
        if (_confirm is { } confirm)
        {
            var (confirmTitle, confirmSubtitle, confirmGlyph, confirmColor) = ConfirmInfo(confirm);
            var chip = new Rectangle(_titleRect.Left, _titleRect.Top + (_titleRect.Height - Px(30)) / 2, Px(30), Px(30));
            DrawChip(g, chip, confirmGlyph, confirmColor, f.IconSmall);
            TextRenderer.DrawText(g, confirmTitle, f.Title,
                Rectangle.FromLTRB(chip.Right + Px(12), _titleRect.Top, _titleRect.Right, _titleRect.Bottom), TextColor,
                TextFlags | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, confirmSubtitle,
                f.Small, _subtitleRect, MutedColor,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix |
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
        else if (_picker is { } picker)
        {
            var (title, glyph, color) = PickerInfo(picker);
            var chip = new Rectangle(_titleRect.Left, _titleRect.Top + (_titleRect.Height - Px(30)) / 2, Px(30), Px(30));
            DrawChip(g, chip, glyph, color, f.IconSmall);
            TextRenderer.DrawText(g, title, f.Title, Rectangle.FromLTRB(chip.Right + Px(12), _titleRect.Top, _titleRect.Right, _titleRect.Bottom),
                TextColor, TextFlags | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, "Elige una opción", f.Small, _subtitleRect, MutedColor, TextFlags);
        }
        else
        {
            var badges = DrawModeBadge(g, f);
            if (DrawBatteryBadge(g, f, badges.Left - Px(8)) is { } battery)
            {
                badges = battery;
            }
            TextRenderer.DrawText(g, "Console Mode", f.Title, Rectangle.FromLTRB(_titleRect.Left, _titleRect.Top, badges.Left - Px(10), _titleRect.Bottom),
                TextColor, TextFlags | TextFormatFlags.EndEllipsis);

            var clockWidth = TextRenderer.MeasureText(_clockText, f.SmallBold, Size.Empty, TextFlags).Width;
            TextRenderer.DrawText(g, _clockText, f.SmallBold, _subtitleRect, TextColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "Cambios temporales: no se guardan en tus modos", f.Small,
                Rectangle.FromLTRB(_subtitleRect.Left, _subtitleRect.Top, _subtitleRect.Right - clockWidth - Px(12), _subtitleRect.Bottom),
                MutedColor, TextFlags | TextFormatFlags.EndEllipsis);
        }

        // ----- Lista -----
        g.SetClip(_viewport);
        for (var i = 0; i < _itemRects.Count; i++)
        {
            var bounds = OnScreen(_itemRects[i]);
            if (bounds.Bottom <= _viewport.Top || bounds.Top >= _viewport.Bottom)
            {
                continue;
            }
            if (Confirming)
            {
                DrawRow(g, f, _confirmRows[i], bounds, i == _confirmSelected);
            }
            else if (_picker != null)
            {
                DrawOption(g, f, _options[i], bounds, i == _optionSelected);
            }
            else
            {
                DrawRow(g, f, _rows[i], bounds, i == _selected);
            }
        }
        g.ResetClip();

        // Flechas cuando hay más opciones por encima o por debajo.
        var arrowRect = new Rectangle(_viewport.Right - Px(24) - Px(30), 0, Px(30), Px(16));
        if (_scroll > 0)
        {
            TextRenderer.DrawText(g, "▲", f.Small, arrowRect with { Y = _viewport.Top - Px(16) }, MutedColor,
                TextFormatFlags.Right | TextFormatFlags.Bottom | TextFormatFlags.NoPadding);
        }
        if (_scroll < _scrollMax)
        {
            TextRenderer.DrawText(g, "▼", f.Small, arrowRect with { Y = _viewport.Bottom }, MutedColor,
                TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        }

        // ----- Pie -----
        if (_status.Length > 0)
        {
            TextRenderer.DrawText(g, _status, f.Small, _statusRect, _statusIsError ? ErrorColor : MutedColor,
                TextFlags | TextFormatFlags.EndEllipsis);
        }
        DrawHints(g, f);
    }

    private void DrawRow(Graphics g, Fonts f, Row row, Rectangle bounds, bool selected)
    {
        if (row.Kind == RowKind.MuteToggles)
        {
            DrawMuteToggles(g, f, bounds, selected);
            return;
        }
        if (row.Kind == RowKind.PowerActions)
        {
            DrawPowerActions(g, f, bounds, selected);
            return;
        }

        FillRound(g, bounds, Px(12), selected ? Accent : Surface);
        var chip = new Rectangle(bounds.Left + Px(14), bounds.Top + (bounds.Height - Px(38)) / 2, Px(38), Px(38));
        var text = TextArea(bounds);
        var titleColor = selected ? Color.FromArgb(225, 225, 255) : MutedColor;

        switch (row.Kind)
        {
            case RowKind.SwitchMode:
            case RowKind.ReturnToGame:
            case RowKind.CloseGame:
                DrawChip(g, chip, row.Glyph, row.Color, f.Icon);
                var middle = bounds.Top + bounds.Height / 2;
                TextRenderer.DrawText(g, row.Title, f.RowBold, Rectangle.FromLTRB(text.Left, middle - f.RowBold.Height, text.Right, middle + Px(1)),
                    TextColor, TextFlags | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, row.Value, f.Small, Rectangle.FromLTRB(text.Left, middle + Px(2), text.Right, middle + Px(2) + f.Small.Height),
                    titleColor, TextFlags | TextFormatFlags.EndEllipsis);
                break;

            case RowKind.ImmersiveToggle:
                DrawChip(g, chip, row.Glyph, row.Color, f.Icon);
                var on = _immersiveEnabled?.Invoke() == true;
                var switchTrack = new Rectangle(text.Right - Px(44), bounds.Top + (bounds.Height - Px(24)) / 2, Px(44), Px(24));
                FillRound(g, switchTrack, switchTrack.Height / 2, on ? (selected ? Color.White : ConsoleColor) : TrackColor);
                var switchKnob = Px(18);
                var switchKnobRect = new Rectangle(on ? switchTrack.Right - switchKnob - Px(3) : switchTrack.Left + Px(3), switchTrack.Top + (switchTrack.Height - switchKnob) / 2, switchKnob, switchKnob);
                using (var switchKnobBrush = new SolidBrush(on ? (selected ? Accent : Color.White) : MutedColor))
                {
                    g.FillEllipse(switchKnobBrush, switchKnobRect);
                }
                var center = bounds.Top + bounds.Height / 2;
                var label = Rectangle.FromLTRB(text.Left, center - f.RowBold.Height, switchTrack.Left - Px(12), center + Px(1));
                TextRenderer.DrawText(g, row.Title, f.RowBold, label, TextColor, TextFlags | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, row.Value, f.Small, label with { Y = center + Px(2), Height = f.Small.Height },
                    titleColor, TextFlags | TextFormatFlags.EndEllipsis);
                break;

            case RowKind.Picker:
                DrawChip(g, chip, row.Glyph, row.Color, f.Icon);
                var top = bounds.Top + (bounds.Height - f.Small.Height - f.Row.Height - Px(2)) / 2;
                var chevronWidth = Px(22);
                TextRenderer.DrawText(g, row.Title, f.Small, new Rectangle(text.Left, top, text.Width - chevronWidth, f.Small.Height),
                    titleColor, TextFlags | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, row.Value, f.Row, new Rectangle(text.Left, top + f.Small.Height + Px(2), text.Width - chevronWidth, f.Row.Height),
                    TextColor, TextFlags | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, GlyphChevron, f.IconSmall, Rectangle.FromLTRB(text.Right - chevronWidth, bounds.Top, text.Right, bounds.Bottom),
                    selected ? TextColor : MutedColor, CenterFlags);
                break;

            case RowKind.Volume:
                var volume = VolumeOf(row.Flow) ?? new VolumeInfo(0, false);
                DrawChip(g, chip, volume.Muted && row.Flow == AudioFlow.Output ? GlyphMute : row.Glyph,
                    volume.Muted ? MutedOffColor : row.Color, f.Icon, slashed: volume.Muted && row.Flow == AudioFlow.Input);
                var titleRect = new Rectangle(text.Left, bounds.Top + Px(10), text.Width, f.Small.Height);
                TextRenderer.DrawText(g, row.Title, f.Small, titleRect, titleColor, TextFlags | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, volume.Muted ? "Silenciado" : $"{volume.Percent} %", f.SmallBold, titleRect,
                    volume.Muted ? ErrorColor : TextColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding |
                    TextFormatFlags.PreserveGraphicsClipping);
                if (VolumeTrack(bounds) is { } track)
                {
                    FillRound(g, track, track.Height / 2, selected ? Color.FromArgb(120, 255, 255, 255) : TrackColor);
                    var filled = track with { Width = (int)Math.Round(track.Width * volume.Percent / 100.0) };
                    if (filled.Width > 0)
                    {
                        FillRound(g, filled, track.Height / 2, volume.Muted ? MutedColor : selected ? Color.White : row.Color);
                    }
                    var knob = Px(16);
                    var knobRect = new Rectangle(track.Left + filled.Width - knob / 2, track.Top + track.Height / 2 - knob / 2, knob, knob);
                    using var knobBrush = new SolidBrush(selected ? Color.White : volume.Muted ? MutedColor : row.Color);
                    g.FillEllipse(knobBrush, knobRect);
                }
                break;
        }
    }

    /// <summary>Dos botones, uno junto a otro: silenciar el sonido y silenciar el micrófono.</summary>
    private void DrawMuteToggles(Graphics g, Fonts f, Rectangle bounds, bool selected)
    {
        var gap = Px(10);
        var width = (bounds.Width - gap) / 2;
        var buttons = new[]
        {
            (Rect: new Rectangle(bounds.Left, bounds.Top, width, bounds.Height), Volume: _outVolume, Input: false),
            (Rect: new Rectangle(bounds.Left + width + gap, bounds.Top, bounds.Width - width - gap, bounds.Height), Volume: _inVolume, Input: true),
        };
        for (var i = 0; i < buttons.Length; i++)
        {
            var (rect, volume, input) = buttons[i];
            var muted = volume?.Muted == true;
            var focused = selected && _toggleIndex == i;
            FillRound(g, rect, Px(12), muted ? Color.FromArgb(70, 239, 68, 68) : Surface);
            if (focused)
            {
                using var pen = new Pen(Accent, Px(3));
                using var path = RoundRect(Rectangle.Inflate(rect, -Px(1), -Px(1)), Px(11));
                g.DrawPath(pen, path);
            }

            var chip = new Rectangle(rect.Left + Px(12), rect.Top + (rect.Height - Px(34)) / 2, Px(34), Px(34));
            if (input)
            {
                DrawChip(g, chip, GlyphMic, muted ? MutedOnColor : MicColor, f.IconSmall, slashed: muted);
            }
            else
            {
                DrawChip(g, chip, muted ? GlyphMute : GlyphVolume, muted ? MutedOnColor : AudioColor, f.IconSmall);
            }

            var label = volume == null ? (input ? "Sin micrófono" : "Sin sonido")
                : input ? (muted ? "Micro silenciado" : "Silenciar micro")
                : muted ? "Sonido silenciado" : "Silenciar sonido";
            TextRenderer.DrawText(g, label, f.SmallBold, Rectangle.FromLTRB(chip.Right + Px(10), rect.Top, rect.Right - Px(8), rect.Bottom),
                volume == null ? MutedColor : muted ? Color.FromArgb(255, 200, 200) : TextColor, TextFlags | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Three buttons side by side: suspend, restart and shut down the PC.</summary>
    private void DrawPowerActions(Graphics g, Fonts f, Rectangle bounds, bool selected)
    {
        var gap = Px(10);
        var count = PowerActions.Length;
        var width = (bounds.Width - gap * (count - 1)) / count;
        for (var i = 0; i < count; i++)
        {
            var left = bounds.Left + i * (width + gap);
            var rect = Rectangle.FromLTRB(left, bounds.Top, i == count - 1 ? bounds.Right : left + width, bounds.Bottom);
            FillRound(g, rect, Px(12), Surface);
            if (selected && _powerIndex == i)
            {
                using var pen = new Pen(Accent, Px(3));
                using var path = RoundRect(Rectangle.Inflate(rect, -Px(1), -Px(1)), Px(11));
                g.DrawPath(pen, path);
            }

            var (label, glyph, color) = PowerInfo(PowerActions[i]);
            var chip = new Rectangle(rect.Left + Px(12), rect.Top + (rect.Height - Px(34)) / 2, Px(34), Px(34));
            DrawChip(g, chip, glyph, color, f.IconSmall);
            TextRenderer.DrawText(g, label, f.SmallBold, Rectangle.FromLTRB(chip.Right + Px(10), rect.Top, rect.Right - Px(8), rect.Bottom),
                TextColor, TextFlags | TextFormatFlags.EndEllipsis);
        }
    }

    private void DrawOption(Graphics g, Fonts f, QuickOption option, Rectangle bounds, bool selected)
    {
        FillRound(g, bounds, Px(12), selected ? Accent : Surface);
        var color = PickerInfo(_picker ?? PickerKind.Audio).Color;

        // Botón de opción: anillo y, si es la actual, punto dentro.
        var size = Px(20);
        var radio = new Rectangle(bounds.Left + Px(16), bounds.Top + (bounds.Height - size) / 2, size, size);
        using (var ring = new Pen(option.IsCurrent ? (selected ? Color.White : color) : MutedColor, Px(2)))
        {
            g.DrawEllipse(ring, radio);
        }
        if (option.IsCurrent)
        {
            using var dot = new SolidBrush(selected ? Color.White : color);
            g.FillEllipse(dot, Rectangle.Inflate(radio, -Px(5), -Px(5)));
        }

        var textRect = Rectangle.FromLTRB(radio.Right + Px(14), bounds.Top, bounds.Right - Px(16), bounds.Bottom);
        if (option.IsCurrent)
        {
            var tagWidth = TextRenderer.MeasureText("Actual", f.SmallBold, Size.Empty, TextFlags).Width + Px(20);
            var tag = new Rectangle(textRect.Right - tagWidth, bounds.Top + (bounds.Height - Px(24)) / 2, tagWidth, Px(24));
            FillRound(g, tag, Px(12), selected ? Color.FromArgb(60, 255, 255, 255) : Color.FromArgb(50, color));
            TextRenderer.DrawText(g, "Actual", f.SmallBold, tag, selected ? Color.White : color, CenterFlags);
            textRect = Rectangle.FromLTRB(textRect.Left, textRect.Top, tag.Left - Px(10), textRect.Bottom);
        }
        TextRenderer.DrawText(g, option.Name, f.Row, textRect, TextColor, TextFlags | TextFormatFlags.EndEllipsis);
    }

    /// <summary>Badge with the current mode, right of the title. Returns where it was drawn.</summary>
    private Rectangle DrawModeBadge(Graphics g, Fonts f)
    {
        var state = _switcher.State;
        var (text, color, glyph) = state.Busy ? ("Cambiando…", MutedColor, GlyphGame)
            : state.Mode == AppMode.Console ? ("Modo consola", ConsoleColor, GlyphGame)
            : ("Modo PC", PcColor, GlyphPc);
        var textWidth = TextRenderer.MeasureText(text, f.SmallBold, Size.Empty, TextFlags).Width;
        var iconWidth = Px(18);
        var width = Px(12) + iconWidth + Px(6) + textWidth + Px(14);
        var height = Px(28);
        var badge = new Rectangle(_titleRect.Right - width, _titleRect.Top + (_titleRect.Height - height) / 2, width, height);
        FillRound(g, badge, height / 2, Color.FromArgb(45, color));
        TextRenderer.DrawText(g, glyph, f.IconTiny, new Rectangle(badge.Left + Px(12), badge.Top, iconWidth, badge.Height), color, CenterFlags);
        TextRenderer.DrawText(g, text, f.SmallBold, Rectangle.FromLTRB(badge.Left + Px(12) + iconWidth + Px(6), badge.Top, badge.Right, badge.Bottom),
            color, TextFlags);
        return badge;
    }

    /// <summary>
    /// Battery of the controller that opened the overlay, with its level drawn, ending at <paramref name="right"/>
    /// (a wired controller shows «Con cable» and no level). Nothing is drawn when the battery is unknown. Returns where
    /// it was drawn.
    /// </summary>
    private Rectangle? DrawBatteryBadge(Graphics g, Fonts f, int right)
    {
        if (_battery is not { } battery)
        {
            return null;
        }
        var color = battery.IsLow ? ErrorColor : TextColor;
        var textWidth = TextRenderer.MeasureText(battery.Label, f.SmallBold, Size.Empty, TextFlags).Width;
        var iconWidth = Px(18);
        var cellWidth = Px(24);
        var width = Px(12) + iconWidth + Px(6) + cellWidth + Px(6) + textWidth + Px(14);
        var height = Px(28);
        var badge = new Rectangle(right - width, _titleRect.Top + (_titleRect.Height - height) / 2, width, height);
        FillRound(g, badge, height / 2, Color.FromArgb(40, color));
        TextRenderer.DrawText(g, GlyphGame, f.IconTiny, new Rectangle(badge.Left + Px(12), badge.Top, iconWidth, badge.Height), color, CenterFlags);

        // Battery cell: outline, terminal and one segment per level (none when wired).
        var cellLeft = badge.Left + Px(12) + iconWidth + Px(6);
        var body = new Rectangle(cellLeft, badge.Top + (badge.Height - Px(12)) / 2, cellWidth - Px(3), Px(12));
        using (var pen = new Pen(color, Math.Max(1f, 1.5f * _dpi / 96f)))
        using (var outline = RoundRect(body, Px(2)))
        {
            g.DrawPath(pen, outline);
        }
        FillRound(g, new Rectangle(body.Right, body.Top + body.Height / 2 - Px(3), Px(3), Px(6)), Px(1), color);
        var inner = Rectangle.Inflate(body, -Px(3), -Px(3));
        var segmentGap = Px(1);
        var segmentWidth = (inner.Width - segmentGap * (ControllerBattery.MaxBars - 1)) / ControllerBattery.MaxBars;
        var bars = battery.Power == ControllerPower.Battery ? battery.Bars : 0;
        for (var i = 0; i < bars; i++)
        {
            FillRound(g, new Rectangle(inner.Left + i * (segmentWidth + segmentGap), inner.Top, segmentWidth, inner.Height), Px(1), color);
        }

        TextRenderer.DrawText(g, battery.Label, f.SmallBold, Rectangle.FromLTRB(body.Right + Px(3) + Px(6), badge.Top, badge.Right, badge.Bottom),
            color, TextFlags);
        return badge;
    }

    /// <summary>Hints at the bottom, with the gamepad buttons drawn with the chosen console's icons.</summary>
    private void DrawHints(Graphics g, Fonts f)
    {
        var hints = new List<(GamepadButtons? Button, string Text)>();
        if (Confirming)
        {
            hints.Add((GamepadButtons.A, "Elegir"));
            hints.Add((GamepadButtons.B, "Cancelar"));
            hints.Add((null, "↑↓  Moverse"));
        }
        else if (_picker != null)
        {
            hints.Add((GamepadButtons.A, "Elegir"));
            hints.Add((GamepadButtons.B, "Volver"));
            hints.Add((null, "↑↓  Moverse"));
        }
        else
        {
            var row = _selected < _rows.Count ? _rows[_selected] : null;
            hints.Add((GamepadButtons.A, row?.Kind switch
            {
                RowKind.Volume => VolumeOf(row.Flow)?.Muted == true ? "Activar" : "Silenciar",
                RowKind.MuteToggles => "Activar / silenciar",
                RowKind.CloseGame => _offerForceClose ? "Forzar cierre" : "Cerrar",
                RowKind.ReturnToGame => "Volver",
                RowKind.ImmersiveToggle => _immersiveEnabled?.Invoke() == true ? "Desactivar" : "Activar",
                _ => "Elegir",
            }));
            hints.Add((GamepadButtons.B, "Cerrar"));
            hints.Add((null, row?.Kind switch
            {
                RowKind.Volume => "←→  Volumen",
                RowKind.MuteToggles or RowKind.PowerActions => "←→  Cambiar",
                _ => "↑↓  Moverse",
            }));
        }

        var x = _hintRect.Left;
        var circle = Px(22);
        foreach (var (button, text) in hints)
        {
            if (button is { } pressed)
            {
                // Drawn with the chosen console's icons: A is «A», «✕» or «B» depending on the family.
                var c = new Rectangle(x, _hintRect.Top + (_hintRect.Height - circle) / 2, circle, circle);
                if (ButtonIconImages.ForDarkBackground(_buttonIcons, pressed) is { } icon)
                {
                    var previous = g.InterpolationMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(icon, Rectangle.Inflate(c, Px(2), Px(2)));
                    g.InterpolationMode = previous;
                }
                else
                {
                    var glyph = ButtonGlyphs.For(_buttonIcons, pressed);
                    using (var brush = new SolidBrush(Color.FromArgb(glyph.Fill.R, glyph.Fill.G, glyph.Fill.B)))
                    {
                        g.FillEllipse(brush, c);
                    }
                    TextRenderer.DrawText(g, glyph.Text, glyph.UsesSymbolFont ? f.ButtonSymbol : f.Button, c,
                        Color.FromArgb(glyph.Foreground.R, glyph.Foreground.G, glyph.Foreground.B), CenterFlags);
                }
                x = c.Right + Px(8);
            }
            var width = TextRenderer.MeasureText(text, f.Small, Size.Empty, TextFlags).Width;
            TextRenderer.DrawText(g, text, f.Small, new Rectangle(x, _hintRect.Top, width + Px(2), _hintRect.Height), MutedColor, TextFlags);
            x += width + Px(22);
        }
    }

    /// <summary>Icono sobre un cuadrado redondeado de color. Con <paramref name="slashed"/> se tacha (micrófono silenciado).</summary>
    private void DrawChip(Graphics g, Rectangle rect, string glyph, Color color, Font font, bool slashed = false)
    {
        FillRound(g, rect, rect.Width / 4, color);
        TextRenderer.DrawText(g, glyph, font, rect, Color.White, CenterFlags);
        if (slashed)
        {
            using var pen = new Pen(Color.White, Math.Max(2, Px(2))) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            var inset = rect.Width / 4;
            g.DrawLine(pen, rect.Left + inset, rect.Top + inset, rect.Right - inset, rect.Bottom - inset);
        }
    }

    private static (string Title, string Glyph, Color Color) PickerInfo(PickerKind picker) => picker switch
    {
        PickerKind.Audio => ("Salida de audio", GlyphVolume, AudioColor),
        PickerKind.Microphone => ("Micrófono", GlyphMic, MicColor),
        PickerKind.Communications => ("Comunicaciones", GlyphHeadset, CommsColor),
        _ => ("Pantalla principal", GlyphDisplay, DisplayColor),
    };

    private static (string Label, string Glyph, Color Color) PowerInfo(ConfirmKind kind) => kind switch
    {
        ConfirmKind.Sleep => ("Suspender", GlyphSleep, SleepColor),
        ConfirmKind.Restart => ("Reiniciar", GlyphRestart, PcColor),
        _ => ("Apagar", GlyphPower, CloseGameColor),
    };

    /// <summary>Header of the confirm dialog.</summary>
    private (string Title, string Subtitle, string Glyph, Color Color) ConfirmInfo(ConfirmKind kind)
    {
        if (kind == ConfirmKind.CloseGame)
        {
            var name = _game?.Name ?? "el juego";
            return _offerForceClose
                ? ($"¿Forzar el cierre de {name}?", "El juego no responde. Se cerrará al momento y perderás lo que no hayas guardado.",
                    GlyphClose, CloseGameColor)
                : ($"¿Cerrar {name}?", "Se cerrará como con Alt+F4. Lo que no hayas guardado puede perderse.", GlyphClose, CloseGameColor);
        }
        var (_, glyph, color) = PowerInfo(kind);
        return kind switch
        {
            ConfirmKind.Sleep => ("¿Suspender el PC?",
                "Los programas se quedan abiertos. Podrás despertarlo con el mando o desde el móvil si lo has preparado.", glyph, color),
            ConfirmKind.Restart => ("¿Reiniciar el PC?",
                "Se cerrarán todos los programas. Lo que no hayas guardado puede perderse.", glyph, color),
            _ => ("¿Apagar el PC?", "Se cerrarán todos los programas. Lo que no hayas guardado puede perderse.", glyph, color),
        };
    }

    private static void FillRound(Graphics g, Rectangle rect, int radius, Color color)
    {
        using var brush = new SolidBrush(color);
        using var path = RoundRect(rect, radius);
        g.FillPath(brush, path);
    }

    private static GraphicsPath RoundRect(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Fuentes en píxeles (1 pt = 1/72") para el DPI dado: WinForms no las reescala.</summary>
    private sealed class Fonts : IDisposable
    {
        private static readonly string IconFamily = IsInstalled("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";

        public Fonts(int dpi)
        {
            Font Make(string family, float points, FontStyle style = FontStyle.Regular) =>
                new(family, points * dpi / 72f, style, GraphicsUnit.Pixel);
            Title = Make("Segoe UI Semibold", 16f);
            Row = Make("Segoe UI", 13.5f);
            RowBold = Make("Segoe UI Semibold", 13.5f);
            Small = Make("Segoe UI", 10f);
            SmallBold = Make("Segoe UI Semibold", 10f);
            Button = Make("Segoe UI", 9f, FontStyle.Bold);
            ButtonSymbol = Make("Segoe UI Symbol", 10f, FontStyle.Bold);
            Icon = Make(IconFamily, 13f);
            IconSmall = Make(IconFamily, 11f);
            IconTiny = Make(IconFamily, 9f);
        }

        public Font Title { get; }
        public Font Row { get; }
        public Font RowBold { get; }
        public Font Small { get; }
        public Font SmallBold { get; }
        public Font Button { get; }
        public Font ButtonSymbol { get; }
        public Font Icon { get; }
        public Font IconSmall { get; }
        public Font IconTiny { get; }

        private static bool IsInstalled(string family)
        {
            using var font = new Font(family, 10f);
            return string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            foreach (var font in new[] { Title, Row, RowBold, Small, SmallBold, Button, ButtonSymbol, Icon, IconSmall, IconTiny })
            {
                font.Dispose();
            }
        }
    }

    // ------------------------------------------------------------------
    // Foco
    // ------------------------------------------------------------------

    /// <summary>
    /// Toma el foco y lo vigila mientras el overlay está abierto. Steam vuelve a poner Big Picture delante de vez en
    /// cuando, y mientras lo tiene se mueve con el mismo mando. Windows avisa al instante de cada cambio de primer
    /// plano y el foco se recupera en ese momento; el temporizador de 100 ms queda como respaldo.
    /// </summary>
    private void GrabFocus()
    {
        _focusClock.Restart();
        Log.Info(WindowFocus.ForceForeground(Handle)
            ? "Overlay: foco obtenido al instante"
            : $"Overlay: no se ha obtenido el foco a la primera (lo tiene {WindowFocus.Describe(WindowFocus.Foreground)})");
        _focusLost = WindowFocus.Foreground != Handle;
        _foregroundWatcher ??= new ForegroundWatcher(OnForegroundChanged);
        _focusTimer.Start();
    }

    private void OnForegroundChanged(IntPtr hwnd)
    {
        if (IsDisposed || hwnd == Handle)
        {
            return;
        }
        if (!ShouldReclaimFocusFrom(hwnd))
        {
            Log.Info($"Overlay: se ha cambiado a {WindowFocus.Describe(hwnd)}; se cierra el overlay");
            Close();
            return;
        }
        Log.Info($"Overlay: {WindowFocus.Describe(hwnd)} le ha quitado el foco; recuperándolo");
        _focusLost = true;
        _focusClock.Restart();
        // Se deja terminar el cambio de foco en curso antes de recuperarlo.
        BeginInvoke(EnsureFocus);
    }

    private void EnsureFocus()
    {
        if (IsDisposed)
        {
            return;
        }

        if (WindowFocus.Foreground == Handle)
        {
            if (_focusLost)
            {
                Log.Info($"Overlay: foco recuperado tras {_focusClock.ElapsedMilliseconds} ms");
                _focusLost = false;
                _focusFailureLogged = false;
            }
            return;
        }

        if (!ShouldReclaimFocusFrom(WindowFocus.Foreground))
        {
            Log.Info($"Overlay: se ha cambiado a {WindowFocus.Describe(WindowFocus.Foreground)}; se cierra el overlay");
            Close();
            return;
        }

        if (!_focusLost)
        {
            // Respaldo: cambio de foco que no ha llegado por el aviso de Windows.
            _focusLost = true;
            _focusClock.Restart();
            Log.Info($"Overlay: {WindowFocus.Describe(WindowFocus.Foreground)} le ha quitado el foco (detectado por el temporizador)");
        }

        if (WindowFocus.ForceForeground(Handle))
        {
            Log.Info($"Overlay: foco recuperado tras {_focusClock.ElapsedMilliseconds} ms");
            _focusLost = false;
            _focusFailureLogged = false;
        }
        else if (!_focusFailureLogged && _focusClock.Elapsed > TimeSpan.FromSeconds(3))
        {
            _focusFailureLogged = true;
            Log.Error($"Overlay: Windows no devuelve el foco al overlay (lo tiene {WindowFocus.Describe(WindowFocus.Foreground)})");
        }
    }

    /// <summary>
    /// Solo se recupera el foco si se lo ha quitado Steam, el frontal (Big Picture, Playnite...) o la ventana que estaba
    /// delante al abrir (el frontal o el juego). Si el usuario cambia a otra aplicación (con el ratón o Alt+Tab), el
    /// overlay se aparta.
    /// </summary>
    private bool ShouldReclaimFocusFrom(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || hwnd == _previousForeground)
        {
            return true;
        }
        var process = WindowFocus.ProcessNameOf(hwnd);
        return process.Length == 0 ||
               string.Equals(process, _previousProcess, StringComparison.OrdinalIgnoreCase) ||
               SteamProcesses.Contains(process) ||
               ConsoleFrontend.ProcessNames.Contains(process, StringComparer.OrdinalIgnoreCase) ||
               ImmersiveGuard.Hides(hwnd); // el modo inmersivo la oculta enseguida: no hay que cerrar el overlay
    }

    private static readonly HashSet<string> SteamProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam",
        "steamwebhelper",
        "gameoverlayui",
    };

    // ------------------------------------------------------------------
    // Cierre sin que el botón llegue a Big Picture
    // ------------------------------------------------------------------

    /// <summary>
    /// Cierra el overlay cuando el mando ya no tiene nada pulsado. Si se cerrase con B aún pulsado, Big Picture
    /// recuperaría el foco y también recibiría ese botón.
    /// </summary>
    private void CloseWhenReleased()
    {
        if (_closing || IsDisposed)
        {
            return;
        }
        _closing = true;
        _idleTimer.Stop();
        _releasedPolls = 0;
        _releaseClock.Restart();
        _releaseTimer.Start();
    }

    private void CheckReleased()
    {
        _releasedPolls = _inputHeld() ? 0 : _releasedPolls + 1;
        // Dos lecturas seguidas sin nada pulsado (~40 ms) o, como mucho, 3 s de espera.
        if (_releasedPolls >= 2 || _releaseClock.Elapsed > TimeSpan.FromSeconds(3))
        {
            _releaseTimer.Stop();
            Close();
        }
    }

    private void OnSwitcherStateChanged(ModeState state)
    {
        if (state.Busy)
        {
            return;
        }
        BeginInvoke(() =>
        {
            if (IsDisposed || !_switchingToPc)
            {
                return;
            }
            _switchingToPc = false;
            _busy = false;
            if (state.Error != null)
            {
                SetStatus(state.Error, error: true);
                _idleTimer.Start();
                ApplyLatestSnapshot();
                return;
            }
            CloseWhenReleased();
        });
    }

    // ------------------------------------------------------------------
    // Posición y escalado
    // ------------------------------------------------------------------

    /// <summary>
    /// Pantalla donde se está jugando: la recién elegida como principal desde el overlay; en modo consola, la pantalla
    /// de la consola (aunque la ventana activa esté en otra, p. ej. un frontal que se abrió en el monitor); si no, la de
    /// la ventana que estaba delante.
    /// </summary>
    private Rectangle TargetArea()
    {
        // Displays come from the cache; their bounds from WinForms' screen list, which is cheap and always current.
        var displays = QuickSwitch.Snapshot;
        if (_targetDisplayGdiName != null &&
            displays?.Displays.FirstOrDefault(d => d.GdiName == _targetDisplayGdiName) is { } target)
        {
            return CurrentBounds(target);
        }
        if (_switcher.State.Mode == AppMode.Console && displays?.FindDisplay(_switcher.ConsoleDisplayId) is { } console)
        {
            return CurrentBounds(console);
        }
        return _previousForeground != IntPtr.Zero
            ? Screen.FromHandle(_previousForeground).Bounds
            : Screen.FromPoint(Cursor.Position).Bounds;
    }

    /// <summary>The display's bounds now, or the cached ones if WinForms does not list it (yet).</summary>
    private static Rectangle CurrentBounds(DisplayInfo display) =>
        Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, display.GdiName, StringComparison.OrdinalIgnoreCase))
            ?.Bounds ?? display.Bounds;

    /// <summary>Vista principal en la esquina inferior derecha de la pantalla de destino; la lista, en el centro.</summary>
    private void Place()
    {
        var area = TargetArea();
        if (Centered)
        {
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
            return;
        }
        var margin = Px(40);
        Location = new Point(area.Right - Width - margin, area.Bottom - Height - margin);
    }

    private static int DpiOf(Rectangle area)
    {
        try
        {
            var monitor = MonitorFromPoint(new POINT { X = area.Left + area.Width / 2, Y = area.Top + area.Height / 2 }, MONITOR_DEFAULTTONEAREST);
            if (GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            {
                return (int)dpiX;
            }
        }
        catch
        {
            // shcore.dll no disponible: escalado del 100 %.
        }
        return 96;
    }

    /// <summary>Píxeles lógicos (96 DPI) a píxeles reales de la pantalla del overlay.</summary>
    private int Px(int value) => (int)Math.Round(value * _dpi / 96f);

    private const int WM_DPICHANGED = 0x02E0;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int MDT_EFFECTIVE_DPI = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
