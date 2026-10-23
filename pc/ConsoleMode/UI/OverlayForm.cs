using System.Runtime.InteropServices;
using ConsoleMode.Core;
using ConsoleMode.Native;

namespace ConsoleMode.UI;

/// <summary>
/// Overlay para el mando en la esquina inferior derecha: cambia la salida de audio y la pantalla principal
/// al momento. Estos cambios son temporales: no se guardan en los perfiles de los modos.
/// </summary>
/// <remarks>
/// Todo se dibuja con el escalado (DPI) de la pantalla donde aparece, con fuentes en píxeles calculadas a mano.
/// Así no depende del escalado de la pantalla principal ni del reescalado automático de WinForms, que hacía que
/// en una TV 4K al 200 % el overlay saliera pequeño y cortado hasta que se reconstruía.
/// </remarks>
public sealed class OverlayForm : Form
{
    private static readonly Color Background = Color.FromArgb(28, 28, 36);
    private static readonly Color Accent = Color.FromArgb(76, 70, 229);
    private static readonly Color TextColor = Color.FromArgb(235, 235, 245);
    private static readonly Color MutedColor = Color.FromArgb(150, 150, 170);
    private static readonly Color ErrorColor = Color.FromArgb(255, 120, 120);
    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(20);

    private enum ItemKind
    {
        Audio,
        Display,
    }

    private sealed record Item(ItemKind Kind, string Id, string Name, bool IsCurrent);

    private readonly System.Windows.Forms.Timer _idleTimer;
    private readonly System.Windows.Forms.Timer _focusTimer = new() { Interval = 50 };
    private readonly System.Diagnostics.Stopwatch _focusClock = new();
    private readonly List<(Item Item, Label Row)> _rows = new();
    private readonly List<Font> _fonts = new();
    private readonly IntPtr _previousForeground;
    private TableLayoutPanel _list = new();
    private Label _status = new();
    private string _statusText = "";
    private bool _statusIsError;
    private int _dpi;
    private int _selected;
    private bool _busy;
    private string? _targetDisplayGdiName;

    public OverlayForm(Icon icon)
    {
        _previousForeground = WindowFocus.Foreground;

        Text = "Console Mode";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Background;
        ForeColor = TextColor;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true;

        // Se construye con el escalado de la pantalla de destino y se coloca ya en su esquina antes de mostrarse,
        // para que la ventana nazca ahí y Windows no le cambie el DPI.
        _dpi = DpiOf(TargetArea());

        _idleTimer = new System.Windows.Forms.Timer { Interval = (int)IdleClose.TotalMilliseconds };
        _idleTimer.Tick += (_, _) => Close();
        _focusTimer.Tick += (_, _) => EnsureFocus();

        BuildUi();
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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Por si Windows ha puesto la ventana en otra pantalla que la calculada.
        if (DeviceDpi != _dpi)
        {
            _dpi = DeviceDpi;
            BuildUi();
        }
        PlaceInCorner();
        GrabFocus();
        _idleTimer.Start();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        // Al pasar a una pantalla con otro escalado lo reconstruimos nosotros, sin el reescalado de WinForms.
        e.Cancel = true;
        if (e.DeviceDpiNew != _dpi)
        {
            _dpi = e.DeviceDpiNew;
            BuildUi();
            PlaceInCorner();
        }
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
            _fonts.ForEach(f => f.Dispose());
            _fonts.Clear();
        }
    }

    // ------------------------------------------------------------------
    // Construcción de la interfaz con el escalado actual (_dpi)
    // ------------------------------------------------------------------

    private void BuildUi()
    {
        SuspendLayout();
        var oldControls = Controls.Cast<Control>().ToList();
        Controls.Clear();
        oldControls.ForEach(c => c.Dispose());
        var oldFonts = _fonts.ToList();
        _fonts.Clear();

        Padding = Pad(22, 18, 22, 18);
        Font = MakeFont(15f);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill, Margin = Padding.Empty };
        layout.Controls.Add(MakeLabel("Console Mode", MakeFont(17f, FontStyle.Bold), TextColor, Pad(0, 0, 0, 0)));
        layout.Controls.Add(MakeLabel("Cambios temporales: no se guardan en tus modos", MakeFont(11f), MutedColor, Pad(0, 2, 0, 12)));

        _list = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        layout.Controls.Add(_list);

        _status = MakeLabel("", MakeFont(11f), MutedColor, Pad(0, 12, 0, 0));
        layout.Controls.Add(_status);
        layout.Controls.Add(MakeLabel("Ⓐ Elegir     Ⓑ Cerrar     ↑↓ Moverse", MakeFont(11f), MutedColor, Pad(0, 6, 0, 0)));
        Controls.Add(layout);

        Reload();
        SetStatus(_statusText, _statusIsError);
        ResumeLayout(true);

        oldFonts.ForEach(f => f.Dispose());
    }

    private void Reload()
    {
        var selectedKey = _rows.Count > 0 && _selected < _rows.Count ? (_rows[_selected].Item.Kind, _rows[_selected].Item.Id) : default;

        var items = new List<Item>();
        try
        {
            items.AddRange(AudioManager.GetOutputDevices().Select(a => new Item(ItemKind.Audio, a.Id, a.Name, a.IsDefault)));
        }
        catch (Exception ex)
        {
            Log.Error("Overlay: no se pudieron leer los dispositivos de audio", ex);
        }
        try
        {
            items.AddRange(DisplayManager.GetDisplays().Select(d => new Item(ItemKind.Display, d.Id, d.FriendlyName, d.IsPrimary)));
        }
        catch (Exception ex)
        {
            Log.Error("Overlay: no se pudieron leer las pantallas", ex);
        }

        _list.SuspendLayout();
        var oldRows = _list.Controls.Cast<Control>().ToList();
        _list.Controls.Clear();
        oldRows.ForEach(c => c.Dispose());
        _rows.Clear();
        AddSection("🔊  Audio", items.Where(i => i.Kind == ItemKind.Audio));
        AddSection("🖥  Pantalla principal", items.Where(i => i.Kind == ItemKind.Display));
        _list.ResumeLayout(true);

        var index = _rows.FindIndex(r => (r.Item.Kind, r.Item.Id) == selectedKey);
        if (index < 0)
        {
            index = Math.Max(0, _rows.FindIndex(r => r.Item.Kind == ItemKind.Audio && r.Item.IsCurrent));
        }
        Select(index);
    }

    private void AddSection(string title, IEnumerable<Item> items)
    {
        var first = _list.Controls.Count == 0;
        _list.Controls.Add(MakeLabel(title, MakeFont(12f, FontStyle.Bold), MutedColor, Pad(0, first ? 0 : 14, 0, 4)));

        var rowFont = MakeFont(15f);
        foreach (var item in items)
        {
            var row = new Label
            {
                Text = $"{(item.IsCurrent ? "●" : "○")}  {item.Name}",
                Font = rowFont,
                AutoSize = false,
                Size = new Size(Px(480), Px(46)),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = Pad(12, 0, 12, 0),
                Margin = Pad(0, 1, 0, 1),
                AutoEllipsis = true,
                UseMnemonic = false,
                Cursor = Cursors.Hand,
            };
            var index = _rows.Count;
            row.Click += (_, _) =>
            {
                Select(index);
                HandleAction(PadAction.Accept);
            };
            _rows.Add((item, row));
            _list.Controls.Add(row);
        }
    }

    private void Select(int index)
    {
        if (_rows.Count == 0)
        {
            return;
        }
        _selected = Math.Clamp(index, 0, _rows.Count - 1);
        for (var i = 0; i < _rows.Count; i++)
        {
            var selected = i == _selected;
            _rows[i].Row.BackColor = selected ? Accent : Background;
            _rows[i].Row.ForeColor = selected ? Color.White : TextColor;
        }
    }

    private void SetStatus(string text, bool error = false)
    {
        _statusText = text;
        _statusIsError = error;
        _status.Text = text;
        _status.ForeColor = error ? ErrorColor : MutedColor;
        _status.Visible = text.Length > 0;
    }

    private async Task ApplySelectedAsync()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var item = _rows[_selected].Item;
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
            Log.Info($"Overlay: {_statusText} (temporal)");
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
        Reload();
        PlaceInCorner();
        GrabFocus();
    }

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
            // shcore.dll no disponible: se usa el escalado del sistema.
        }
        return 96;
    }

    /// <summary>Píxeles lógicos (96 DPI) a píxeles reales de la pantalla del overlay.</summary>
    private int Px(int value) => (int)Math.Round(value * _dpi / 96f);

    private Padding Pad(int left, int top, int right, int bottom) => new(Px(left), Px(top), Px(right), Px(bottom));

    /// <summary>Fuente en píxeles (1 pt = 1/72") para que WinForms no la reescale con el DPI del sistema.</summary>
    private Font MakeFont(float points, FontStyle style = FontStyle.Regular)
    {
        var family = style.HasFlag(FontStyle.Bold) ? "Segoe UI Semibold" : "Segoe UI";
        var font = new Font(family, points * _dpi / 72f, style & ~FontStyle.Bold, GraphicsUnit.Pixel);
        _fonts.Add(font);
        return font;
    }

    private static Label MakeLabel(string text, Font font, Color color, Padding margin) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        AutoSize = true,
        Margin = margin,
        UseMnemonic = false,
    };

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
