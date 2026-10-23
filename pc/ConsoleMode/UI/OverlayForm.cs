using System.Runtime.InteropServices;
using ConsoleMode.Core;
using ConsoleMode.Native;

namespace ConsoleMode.UI;

/// <summary>
/// Overlay para el mando en la esquina inferior derecha: cambia la salida de audio y la pantalla principal
/// al momento. Estos cambios son temporales: no se guardan en los perfiles de los modos.
/// </summary>
public sealed class OverlayForm : Form
{
    private static readonly Color Background = Color.FromArgb(28, 28, 36);
    private static readonly Color Accent = Color.FromArgb(76, 70, 229);
    private static readonly Color TextColor = Color.FromArgb(235, 235, 245);
    private static readonly Color MutedColor = Color.FromArgb(150, 150, 170);
    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(20);

    private enum ItemKind
    {
        Audio,
        Display,
    }

    private sealed record Item(ItemKind Kind, string Id, string Name, bool IsCurrent);

    private readonly TableLayoutPanel _list;
    private readonly Label _status;
    private readonly System.Windows.Forms.Timer _idleTimer;
    private readonly List<(Item Item, Label Row)> _rows = new();
    private readonly IntPtr _previousForeground;
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
        BackColor = Background;
        ForeColor = TextColor;
                Font = new Font("Segoe UI", 15f);
        Padding = Pad(20, 16, 20, 16);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true;

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label
        {
            Text = "Console Mode",
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 17f),
            Margin = Pad(0, 0, 0, 0),
        });
        layout.Controls.Add(new Label
        {
            Text = "Cambios temporales: no se guardan en tus modos",
            AutoSize = true,
            ForeColor = MutedColor,
            Font = new Font("Segoe UI", 11f),
            Margin = Pad(0, 0, 0, 10),
        });

        _list = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        layout.Controls.Add(_list);

        _status = new Label { AutoSize = true, ForeColor = MutedColor, Font = new Font("Segoe UI", 11f), Margin = Pad(0, 10, 0, 0) };
        layout.Controls.Add(_status);
        layout.Controls.Add(new Label
        {
            Text = "Ⓐ Elegir     Ⓑ Cerrar     ↑↓ Moverse",
            AutoSize = true,
            ForeColor = MutedColor,
            Font = new Font("Segoe UI", 11f),
            Margin = Pad(0, 6, 0, 0),
        });
        Controls.Add(layout);

        _idleTimer = new System.Windows.Forms.Timer { Interval = (int)IdleClose.TotalMilliseconds };
        _idleTimer.Tick += (_, _) => Close();

        Reload();
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
        PlaceInCorner();
        WindowFocus.ForceForeground(Handle);
        _idleTimer.Start();
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
        if (!_busy)
        {
            BeginInvoke(Close);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _idleTimer.Dispose();
        // Solo devolvemos el foco si seguía en el overlay (si el usuario hizo clic en otra ventana, la respetamos).
        if (WindowFocus.Foreground == Handle)
        {
            WindowFocus.Restore(_previousForeground);
        }
        base.OnFormClosed(e);
    }

    // ------------------------------------------------------------------

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
        _list.Controls.Clear();
        _rows.Clear();
        AddSection("🔊  Audio", items.Where(i => i.Kind == ItemKind.Audio));
        AddSection("🖥  Pantalla principal", items.Where(i => i.Kind == ItemKind.Display));
        _list.ResumeLayout();

        var index = _rows.FindIndex(r => (r.Item.Kind, r.Item.Id) == selectedKey);
        if (index < 0)
        {
            index = Math.Max(0, _rows.FindIndex(r => r.Item.Kind == ItemKind.Audio && r.Item.IsCurrent));
        }
        Select(index);
    }

    private void AddSection(string title, IEnumerable<Item> items)
    {
        _list.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            ForeColor = MutedColor,
            Font = new Font("Segoe UI Semibold", 12f),
            Margin = Pad(0, _rows.Count == 0 ? 0 : 12, 0, 4),
        });

        foreach (var item in items)
        {
            var row = new Label
            {
                Text = $"{(item.IsCurrent ? "●" : "○")}  {item.Name}",
                AutoSize = false,
                Size = new Size(Px(460), Px(44)),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = Pad(10, 0, 10, 0),
                Margin = Pad(0, 1, 0, 1),
                AutoEllipsis = true,
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

    private async Task ApplySelectedAsync()
    {
        if (_rows.Count == 0)
        {
            return;
        }

        var item = _rows[_selected].Item;
        _busy = true;
        _status.ForeColor = MutedColor;
        _status.Text = "Aplicando…";
        try
        {
            if (item.Kind == ItemKind.Audio)
            {
                await Task.Run(() => AudioManager.SetDefaultOutput(item.Id));
                _status.Text = $"Audio: {item.Name}";
            }
            else
            {
                await Task.Run(() => DisplayManager.SetPrimary(item.Id));
                _targetDisplayGdiName = DisplayManager.Find(item.Id)?.GdiName;
                _status.Text = $"Pantalla principal: {item.Name}";
                if (SteamBigPicture.IsOpen)
                {
                    _ = Task.Run(() => SteamBigPicture.EnsureOnDisplayAsync(item.Id, TimeSpan.FromSeconds(5)));
                }
            }
            Log.Info($"Overlay: {_status.Text} (temporal)");
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.FromArgb(255, 120, 120);
            _status.Text = ex.Message;
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
        WindowFocus.ForceForeground(Handle);
    }

    /// <summary>Esquina inferior derecha de la pantalla donde se está jugando (o de la recién elegida).</summary>
    private void PlaceInCorner()
    {
        Rectangle area;
        var target = _targetDisplayGdiName == null
            ? null
            : DisplayManager.GetDisplays().FirstOrDefault(d => d.GdiName == _targetDisplayGdiName);
        if (target != null)
        {
            area = target.Bounds;
        }
        else
        {
            area = _previousForeground != IntPtr.Zero ? Screen.FromHandle(_previousForeground).Bounds : Screen.FromPoint(Cursor.Position).Bounds;
        }

        var margin = Px(40);
        Location = new Point(area.Right - Width - margin, area.Bottom - Height - margin);
    }

    /// <summary>Píxeles lógicos (96 DPI) a píxeles reales: en una TV 4K al 200 % todo se ve al doble.</summary>
    private int Px(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    private Padding Pad(int left, int top, int right, int bottom) => new(Px(left), Px(top), Px(right), Px(bottom));

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
