using System.Runtime.InteropServices;
using ConsoleMode.Core;
using ConsoleMode.Native;

namespace ConsoleMode.UI;

/// <summary>
/// Overlay para el mando en la esquina inferior derecha: cambia la salida de audio y la pantalla principal
/// al momento. Estos cambios son temporales: no se guardan en los perfiles de los modos.
/// </summary>
/// <remarks>
/// Se dibuja entero a mano (sin controles hijos) con el escalado (DPI) de la pantalla donde aparece.
/// Con controles normales, WinForms los reescalaba por su cuenta al llegar a una TV 4K al 200 % y el texto
/// salía pequeño, o al doble y cortado, hasta que se reconstruían.
/// </remarks>
public sealed class OverlayForm : Form
{
    private static readonly Color Background = Color.FromArgb(28, 28, 36);
    private static readonly Color Border = Color.FromArgb(60, 60, 76);
    private static readonly Color Accent = Color.FromArgb(76, 70, 229);
    private static readonly Color TextColor = Color.FromArgb(235, 235, 245);
    private static readonly Color MutedColor = Color.FromArgb(150, 150, 170);
    private static readonly Color ErrorColor = Color.FromArgb(255, 120, 120);
    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(20);

    private const TextFormatFlags TextFlags =
        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    private enum ItemKind
    {
        SwitchToPc,
        Audio,
        Display,
    }

    private sealed record Item(ItemKind Kind, string Id, string Name, bool IsCurrent);

    /// <summary>Una línea del overlay ya colocada: cabecera de sección o fila seleccionable.</summary>
    private sealed record Line(Rectangle Bounds, string Text, Font Font, Color Color, int RowIndex = -1);

    private readonly System.Windows.Forms.Timer _idleTimer;
    private readonly System.Windows.Forms.Timer _focusTimer = new() { Interval = 50 };
    private readonly System.Diagnostics.Stopwatch _focusClock = new();
    private readonly IntPtr _previousForeground;
    private readonly ModeSwitcher _switcher;
    private readonly List<Item> _items = new();
    private readonly List<Line> _lines = new();
    private Fonts? _fonts;
    private string _status = "";
    private bool _statusIsError;
    private int _dpi;
    private int _selected;
    private bool _busy;
    private string? _targetDisplayGdiName;

    public OverlayForm(Icon icon, ModeSwitcher switcher)
    {
        _previousForeground = WindowFocus.Foreground;
        _switcher = switcher;

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
        _idleTimer.Tick += (_, _) => Close();
        _focusTimer.Tick += (_, _) => EnsureFocus();

        // Se calcula con el escalado de la pantalla de destino y se coloca ya en su esquina antes de mostrarse.
        _dpi = DpiOf(TargetArea());
        LoadItems();
        Relayout();
        PlaceInCorner();
    }

    /// <summary>Acción del mando (o del teclado) mientras el overlay está abierto.</summary>
    public void HandleAction(PadAction action)
    {
        _idleTimer.Stop();
        _idleTimer.Start();
        if (_busy)
        {
            return;
        }

        switch (action)
        {
            case PadAction.Up:
                Select(_selected - 1);
                break;
            case PadAction.Down:
                Select(_selected + 1);
                break;
            case PadAction.Accept:
                _ = ApplySelectedAsync();
                break;
            case PadAction.Back:
            case PadAction.ToggleOverlay:
                Close();
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
        PlaceInCorner();
        GrabFocus();
        _idleTimer.Start();
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
                PlaceInCorner();
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
        PadAction? action = e.KeyCode switch
        {
            Keys.Up => PadAction.Up,
            Keys.Down => PadAction.Down,
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

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        var line = _lines.FirstOrDefault(l => l.RowIndex >= 0 && l.Bounds.Contains(e.Location));
        if (line != null)
        {
            Select(line.RowIndex);
            HandleAction(PadAction.Accept);
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // Si el usuario hace clic en otro sitio, el overlay se va.
        if (!_busy && !_focusTimer.Enabled)
        {
            BeginInvoke(Close);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _idleTimer.Dispose();
        _focusTimer.Dispose();
        // Solo devolvemos el foco si seguía en el overlay (si el usuario hizo clic en otra ventana, la respetamos).
        if (WindowFocus.Foreground == Handle)
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
            _fonts?.Dispose();
            _fonts = null;
        }
    }

    // ------------------------------------------------------------------
    // Datos
    // ------------------------------------------------------------------

    private void LoadItems()
    {
        var selectedKey = _selected < _items.Count ? (_items[_selected].Kind, _items[_selected].Id) : default;

        _items.Clear();
        var state = _switcher.State;
        if (state.Mode == AppMode.Console && !state.Busy)
        {
            _items.Add(new Item(ItemKind.SwitchToPc, "pc", "Volver a modo PC", false));
        }
        try
        {
            _items.AddRange(AudioManager.GetOutputDevices().Select(a => new Item(ItemKind.Audio, a.Id, a.Name, a.IsDefault)));
        }
        catch (Exception ex)
        {
            Log.Error("Overlay: no se pudieron leer los dispositivos de audio", ex);
        }
        try
        {
            _items.AddRange(DisplayManager.GetDisplays().Select(d => new Item(ItemKind.Display, d.Id, d.FriendlyName, d.IsPrimary)));
        }
        catch (Exception ex)
        {
            Log.Error("Overlay: no se pudieron leer las pantallas", ex);
        }

        var index = _items.FindIndex(i => (i.Kind, i.Id) == selectedKey);
        if (index < 0)
        {
            // Se empieza en el audio actual (nunca en «Volver a modo PC», para que un A sin querer no cambie de modo).
            index = _items.FindIndex(i => i.Kind == ItemKind.Audio && i.IsCurrent);
            if (index < 0)
            {
                index = Math.Max(0, _items.FindIndex(i => i.Kind != ItemKind.SwitchToPc));
            }
        }
        _selected = Math.Clamp(index, 0, Math.Max(0, _items.Count - 1));
    }

    private void Select(int index)
    {
        if (_items.Count == 0)
        {
            return;
        }
        _selected = Math.Clamp(index, 0, _items.Count - 1);
        Invalidate();
    }

    private void SetStatus(string text, bool error = false)
    {
        var sizeChanged = (_status.Length == 0) != (text.Length == 0);
        _status = text;
        _statusIsError = error;
        if (sizeChanged)
        {
            Relayout();
            PlaceInCorner();
        }
        Invalidate();
    }

    private async Task ApplySelectedAsync()
    {
        if (_items.Count == 0)
        {
            return;
        }

        var item = _items[_selected];
        if (item.Kind == ItemKind.SwitchToPc)
        {
            // Se cierra primero para devolver el foco; el cambio de modo cierra Big Picture y restaura el modo PC.
            Log.Info("Overlay: volver a modo PC");
            Close();
            _ = _switcher.SwitchAsync(AppMode.Pc);
            return;
        }

        _busy = true;
        SetStatus("Aplicando…");
        try
        {
            if (item.Kind == ItemKind.Audio)
            {
                await Task.Run(() => AudioManager.SetDefaultOutput(item.Id));
                SetStatus($"Audio: {item.Name}");
            }
            else
            {
                await Task.Run(() => DisplayManager.SetPrimary(item.Id));
                _targetDisplayGdiName = DisplayManager.Find(item.Id)?.GdiName;
                SetStatus($"Pantalla principal: {item.Name}");
                if (SteamBigPicture.IsOpen)
                {
                    _ = Task.Run(() => SteamBigPicture.EnsureOnDisplayAsync(item.Id, TimeSpan.FromSeconds(5)));
                }
            }
            Log.Info($"Overlay: {_status} (temporal)");
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, error: true);
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
        var dpi = DpiOf(TargetArea());
        if (dpi != _dpi)
        {
            _dpi = dpi;
        }
        LoadItems();
        Relayout();
        PlaceInCorner();
        GrabFocus();
    }

    // ------------------------------------------------------------------
    // Disposición y dibujo
    // ------------------------------------------------------------------

    /// <summary>Calcula la posición de cada línea y el tamaño de la ventana con el DPI actual.</summary>
    private void Relayout()
    {
        _fonts?.Dispose();
        _fonts = new Fonts(_dpi);
        var f = _fonts;
        _lines.Clear();

        var padX = Px(24);
        var rowPadX = Px(14);
        var rowHeight = Px(48);
        var bulletWidth = TextRenderer.MeasureText("●  ", f.Row, Size.Empty, TextFlags).Width;

        // Ancho: el del texto más largo, entre 420 y 680 px lógicos.
        var widest = new[]
            {
                TextRenderer.MeasureText("Console Mode", f.Title, Size.Empty, TextFlags).Width,
                TextRenderer.MeasureText(Subtitle, f.Small, Size.Empty, TextFlags).Width,
                TextRenderer.MeasureText(Hint, f.Small, Size.Empty, TextFlags).Width,
            }
            .Concat(_items.Select(i => bulletWidth + TextRenderer.MeasureText(i.Name, f.Row, Size.Empty, TextFlags).Width + rowPadX * 2))
            .Max();
        var contentWidth = Math.Clamp(widest, Px(420), Px(680));

        var y = Px(20);
        void Add(string text, Font font, Color color, int height, int marginTop = 0, int rowIndex = -1)
        {
            y += marginTop;
            _lines.Add(new Line(new Rectangle(padX, y, contentWidth, height), text, font, color, rowIndex));
            y += height;
        }
        int LineHeight(Font font) => TextRenderer.MeasureText("Ág", font, Size.Empty, TextFlags).Height;

        Add("Console Mode", f.Title, TextColor, LineHeight(f.Title));
        Add(Subtitle, f.Small, MutedColor, LineHeight(f.Small), Px(2));

        var sections = new[]
        {
            (ItemKind.SwitchToPc, "🎮  Modo consola activo"),
            (ItemKind.Audio, "🔊  Audio"),
            (ItemKind.Display, "🖥  Pantalla principal"),
        };
        foreach (var (kind, title) in sections)
        {
            if (!_items.Any(i => i.Kind == kind))
            {
                continue;
            }
            Add(title, f.Section, MutedColor, LineHeight(f.Section), Px(18));
            y += Px(4);
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Kind == kind)
                {
                    Add(_items[i].Name, f.Row, TextColor, rowHeight, Px(2), i);
                }
            }
        }

        if (_status.Length > 0)
        {
            Add(_status, f.Small, MutedColor, LineHeight(f.Small), Px(14));
        }
        Add(Hint, f.Small, MutedColor, LineHeight(f.Small), Px(_status.Length > 0 ? 6 : 14));

        ClientSize = new Size(contentWidth + padX * 2, y + Px(20));
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Background);
        using (var border = new Pen(Border, Math.Max(1, Px(1))))
        {
            g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
        if (_fonts == null)
        {
            return;
        }

        var bulletWidth = TextRenderer.MeasureText("●  ", _fonts.Row, Size.Empty, TextFlags).Width;
        var rowPadX = Px(14);

        foreach (var line in _lines)
        {
            if (line.RowIndex < 0)
            {
                var color = line.Text == _status && _statusIsError ? ErrorColor : line.Color;
                TextRenderer.DrawText(g, line.Text, line.Font, line.Bounds, color, TextFlags | TextFormatFlags.EndEllipsis);
                continue;
            }

            var item = _items[line.RowIndex];
            var selected = line.RowIndex == _selected;
            if (selected)
            {
                using var brush = new SolidBrush(Accent);
                g.FillRectangle(brush, line.Bounds);
            }
            var fore = selected ? Color.White : TextColor;
            var bulletRect = new Rectangle(line.Bounds.X + rowPadX, line.Bounds.Y, bulletWidth, line.Bounds.Height);
            var textRect = Rectangle.FromLTRB(bulletRect.Right, line.Bounds.Y, line.Bounds.Right - rowPadX, line.Bounds.Bottom);
            var bullet = item.Kind == ItemKind.SwitchToPc ? "↩" : item.IsCurrent ? "●" : "○";
            TextRenderer.DrawText(g, bullet, line.Font, bulletRect, fore, TextFlags);
            TextRenderer.DrawText(g, item.Name, line.Font, textRect, fore, TextFlags | TextFormatFlags.EndEllipsis);
        }
    }

    private const string Subtitle = "Cambios temporales: no se guardan en tus modos";
    private const string Hint = "Ⓐ Elegir     Ⓑ Cerrar     ↑↓ Moverse";

    /// <summary>Fuentes en píxeles (1 pt = 1/72") para el DPI dado: WinForms no las reescala.</summary>
    private sealed class Fonts : IDisposable
    {
        public Fonts(int dpi)
        {
            Font Make(string family, float points) => new(family, points * dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
            Title = Make("Segoe UI Semibold", 17f);
            Section = Make("Segoe UI Semibold", 12f);
            Row = Make("Segoe UI", 15f);
            Small = Make("Segoe UI", 11f);
        }

        public Font Title { get; }
        public Font Section { get; }
        public Font Row { get; }
        public Font Small { get; }

        public void Dispose()
        {
            Title.Dispose();
            Section.Dispose();
            Row.Dispose();
            Small.Dispose();
        }
    }

    // ------------------------------------------------------------------
    // Foco
    // ------------------------------------------------------------------

    /// <summary>
    /// Toma el foco y, si Windows no lo concede a la primera, reintenta cada 50 ms durante 3 s. Mientras Big Picture
    /// tenga el foco, Steam sigue moviéndose con el mismo mando, así que esto tiene que ser inmediato.
    /// </summary>
    private void GrabFocus()
    {
        _focusClock.Restart();
        if (WindowFocus.ForceForeground(Handle))
        {
            Log.Info("Overlay: foco obtenido al instante");
            return;
        }
        _focusTimer.Start();
    }

    private void EnsureFocus()
    {
        if (WindowFocus.ForceForeground(Handle))
        {
            _focusTimer.Stop();
            Log.Info($"Overlay: foco obtenido tras {_focusClock.ElapsedMilliseconds} ms");
        }
        else if (_focusClock.Elapsed > TimeSpan.FromSeconds(3))
        {
            _focusTimer.Stop();
            Log.Error($"Overlay: Windows no ha dado el foco al overlay (lo tiene la ventana 0x{WindowFocus.Foreground:X})");
        }
    }

    // ------------------------------------------------------------------
    // Posición y escalado
    // ------------------------------------------------------------------

    /// <summary>Pantalla donde se está jugando (o la recién elegida como principal).</summary>
    private Rectangle TargetArea()
    {
        if (_targetDisplayGdiName != null)
        {
            var target = DisplayManager.GetDisplays().FirstOrDefault(d => d.GdiName == _targetDisplayGdiName);
            if (target != null)
            {
                return target.Bounds;
            }
        }
        return _previousForeground != IntPtr.Zero
            ? Screen.FromHandle(_previousForeground).Bounds
            : Screen.FromPoint(Cursor.Position).Bounds;
    }

    /// <summary>Esquina inferior derecha de la pantalla de destino.</summary>
    private void PlaceInCorner()
    {
        var area = TargetArea();
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

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
