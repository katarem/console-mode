using System.Diagnostics;
using ConsoleMode.Core;
using ConsoleMode.Native;
using ConsoleMode.Network;
using QRCoder;

namespace ConsoleMode.UI;

/// <summary>Ventana principal: interruptor PC/Consola, perfiles de cada modo, móviles y ajustes.</summary>
public sealed class MainForm : Form
{
    private static readonly Color Accent = Color.FromArgb(76, 70, 229);
    private static readonly Color Inactive = Color.FromArgb(230, 230, 236);

    private readonly TrayApplicationContext _app;
    private AppSettings Settings => _app.Settings;

    private readonly Button _pcButton;
    private readonly Button _consoleButton;
    private readonly Label _modeLabel;
    private readonly Label _statusLabel;

    private readonly ComboBox _pcDisplay = NewCombo();
    private readonly ComboBox _pcAudio = NewCombo();
    private readonly ComboBox _consoleDisplay = NewCombo();
    private readonly ComboBox _consoleAudio = NewCombo();
    private readonly CheckBox _launchBigPicture = new() { Text = "Abrir Steam Big Picture al activar el modo consola", AutoSize = true };
    private readonly CheckBox _moveBigPicture = new() { Text = "Forzar Big Picture en la pantalla del modo consola", AutoSize = true };
    private readonly CheckBox _closeBigPicture = new() { Text = "Cerrar Big Picture al volver al modo PC", AutoSize = true };

    private readonly PictureBox _qr = new() { Size = new Size(220, 220), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
    private readonly Label _addressLabel = new() { AutoSize = true, MaximumSize = new Size(260, 0) };
    private readonly ListView _devices = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        Dock = DockStyle.Fill,
    };

    private readonly CheckBox _showTray = new() { Text = "Mostrar icono en la bandeja del sistema (iconos ocultos)", AutoSize = true };
    private readonly CheckBox _startWithWindows = new() { Text = "Iniciar con Windows (en segundo plano)", AutoSize = true };
    private readonly NumericUpDown _port = new() { Minimum = 1024, Maximum = 65535, Width = 90 };

    private bool _loading;

    public MainForm(TrayApplicationContext app)
    {
        _app = app;

        Text = $"Console Mode {Application.ProductVersion.Split('+')[0]}";
        Icon = app.AppIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 600);
        MinimumSize = new Size(560, 560);

        // ---------- Interruptor PC / Consola ----------
        _modeLabel = new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 13f), Margin = new Padding(0, 0, 0, 8) };
        _pcButton = NewModeButton("🖥  Modo PC");
        _consoleButton = NewModeButton("🎮  Modo Consola");
        _pcButton.Click += (_, _) => _ = _app.Switcher.SwitchAsync(AppMode.Pc);
        _consoleButton.Click += (_, _) => _ = _app.Switcher.SwitchAsync(AppMode.Console);

        var switchRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        switchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        switchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        switchRow.Controls.Add(_pcButton, 0, 0);
        switchRow.Controls.Add(_consoleButton, 1, 0);

        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(12, 12, 12, 4) };
        header.Controls.Add(_modeLabel);
        header.Controls.Add(switchRow);

        _statusLabel = new Label { Dock = DockStyle.Bottom, AutoSize = false, Height = 28, Padding = new Padding(12, 6, 12, 0), ForeColor = Color.DimGray };

        // ---------- Pestañas ----------
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) };
        tabs.TabPages.Add(BuildModesTab());
        var devicesTab = BuildDevicesTab();
        tabs.TabPages.Add(devicesTab);
        tabs.TabPages.Add(BuildSettingsTab());
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (tabs.SelectedTab == devicesTab)
            {
                RefreshQr();
                RefreshDevices();
            }
        };

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 8, 12, 4) };
        body.Controls.Add(tabs);

        Controls.Add(body);
        Controls.Add(_statusLabel);
        Controls.Add(header);

        LoadValues();
        UpdateModeState(_app.Switcher.State);
        if (_app.ServerError != null)
        {
            SetStatus(_app.ServerError, error: true);
        }
    }

    // =====================================================================================
    // Pestañas
    // =====================================================================================

    private TabPage BuildModesTab()
    {
        var page = new TabPage("Modos") { Padding = new Padding(10), AutoScroll = true };
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var pcGroup = NewGroup("🖥  Modo PC");
        var pcGrid = NewGrid();
        AddRow(pcGrid, "Pantalla principal:", _pcDisplay);
        AddRow(pcGrid, "Salida de audio:", _pcAudio);
        pcGroup.Controls.Add(pcGrid);

        var consoleGroup = NewGroup("🎮  Modo Consola");
        var consoleGrid = NewGrid();
        AddRow(consoleGrid, "Pantalla principal:", _consoleDisplay);
        AddRow(consoleGrid, "Salida de audio:", _consoleAudio);
        consoleGrid.Controls.Add(_launchBigPicture);
        consoleGrid.SetColumnSpan(_launchBigPicture, 2);
        consoleGrid.Controls.Add(_moveBigPicture);
        consoleGrid.SetColumnSpan(_moveBigPicture, 2);
        consoleGrid.Controls.Add(_closeBigPicture);
        consoleGrid.SetColumnSpan(_closeBigPicture, 2);
        consoleGroup.Controls.Add(consoleGrid);

        var refresh = new Button { Text = "Actualizar pantallas y audio", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        refresh.Click += (_, _) =>
        {
            LoadDeviceLists();
            SetStatus("Lista de pantallas y dispositivos de audio actualizada.");
        };

        var hint = new Label
        {
            Text = "«(No cambiar)» deja la pantalla o el audio como estén. Big Picture se abre en la pantalla principal; " +
                   "si Steam la abre en otra, Console Mode la mueve a la del modo consola.",
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(3, 8, 3, 3),
        };

        layout.Controls.Add(pcGroup);
        layout.Controls.Add(consoleGroup);
        layout.Controls.Add(refresh);
        layout.Controls.Add(hint);
        page.Controls.Add(layout);

        _pcDisplay.SelectedIndexChanged += (_, _) => OnDisplayChanged(_pcDisplay, Settings.PcProfile);
        _consoleDisplay.SelectedIndexChanged += (_, _) => OnDisplayChanged(_consoleDisplay, Settings.ConsoleProfile);
        _pcAudio.SelectedIndexChanged += (_, _) => OnAudioChanged(_pcAudio, Settings.PcProfile);
        _consoleAudio.SelectedIndexChanged += (_, _) => OnAudioChanged(_consoleAudio, Settings.ConsoleProfile);
        _launchBigPicture.CheckedChanged += (_, _) => SaveIfNotLoading(() =>
        {
            Settings.LaunchBigPicture = _launchBigPicture.Checked;
            _moveBigPicture.Enabled = _launchBigPicture.Checked;
        });
        _moveBigPicture.CheckedChanged += (_, _) => SaveIfNotLoading(() => Settings.MoveBigPictureToConsoleDisplay = _moveBigPicture.Checked);
        _closeBigPicture.CheckedChanged += (_, _) => SaveIfNotLoading(() => Settings.CloseBigPictureOnPcMode = _closeBigPicture.Checked);

        return page;
    }

    private TabPage BuildDevicesTab()
    {
        var page = new TabPage("Móviles") { Padding = new Padding(10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var qrInfo = new Label
        {
            Text = "Escanea este código con la app Console Mode del móvil, o búscalo desde la app con «Buscar en la red».\n\n" +
                   "La primera vez tendrás que aceptar el dispositivo en este PC; después quedará guardado como de confianza.",
            AutoSize = true,
            MaximumSize = new Size(260, 0),
            Margin = new Padding(12, 0, 0, 12),
        };
        var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        right.Controls.Add(qrInfo);
        right.Controls.Add(_addressLabel);
        _addressLabel.Margin = new Padding(12, 0, 0, 0);
        _addressLabel.Font = new Font(Font, FontStyle.Bold);

        layout.Controls.Add(_qr, 0, 0);
        layout.Controls.Add(right, 1, 0);

        var devicesGroup = NewGroup("Dispositivos de confianza");
        devicesGroup.Dock = DockStyle.Fill;
        devicesGroup.AutoSize = false;
        _devices.Columns.Add("Nombre", 200);
        _devices.Columns.Add("Añadido", 130);
        _devices.Columns.Add("Última conexión", 140);

        var remove = new Button { Text = "Quitar dispositivo", AutoSize = true, Dock = DockStyle.Bottom, Enabled = false };
        _devices.SelectedIndexChanged += (_, _) => remove.Enabled = _devices.SelectedItems.Count > 0;
        remove.Click += (_, _) =>
        {
            if (_devices.SelectedItems.Count == 0 || _devices.SelectedItems[0].Tag is not string id)
            {
                return;
            }
            var name = _devices.SelectedItems[0].Text;
            if (MessageBox.Show(this, $"¿Quitar «{name}» de los dispositivos de confianza?\nTendrá que volver a emparejarse.",
                    "Console Mode", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _app.RemoveTrustedDevice(id);
                RefreshDevices();
            }
        };
        devicesGroup.Controls.Add(_devices);
        devicesGroup.Controls.Add(remove);

        layout.Controls.Add(devicesGroup, 0, 1);
        layout.SetColumnSpan(devicesGroup, 2);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildSettingsTab()
    {
        var page = new TabPage("Ajustes") { Padding = new Padding(10), AutoScroll = true };
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };

        var trayHint = new Label
        {
            Text = "Console Mode siempre sigue funcionando en segundo plano al cerrar esta ventana. " +
                   "Si ocultas el icono, vuelve a abrir Console Mode (menú Inicio o el .exe) para ver esta ventana.",
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            ForeColor = Color.DimGray,
            Margin = new Padding(22, 0, 3, 12),
        };

        var portRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 12) };
        portRow.Controls.Add(new Label { Text = "Puerto de escucha:", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
        portRow.Controls.Add(_port);
        portRow.Controls.Add(new Label { Text = "(se aplica al reiniciar la app)", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(6, 6, 3, 3) });

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 12, 0, 0) };
        var openFolder = new Button { Text = "Abrir carpeta de configuración", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        openFolder.Click += (_, _) => Process.Start(new ProcessStartInfo(AppSettings.Directory) { UseShellExecute = true });
        var exit = new Button { Text = "Salir de Console Mode", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        exit.Click += (_, _) => _app.Exit();
        buttons.Controls.Add(openFolder);
        buttons.Controls.Add(exit);

        layout.Controls.Add(_showTray);
        layout.Controls.Add(trayHint);
        layout.Controls.Add(_startWithWindows);
        layout.Controls.Add(new Label { AutoSize = true, Height = 8 });
        layout.Controls.Add(portRow);
        layout.Controls.Add(buttons);
        page.Controls.Add(layout);

        _showTray.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }
            _app.SetTrayIconVisible(_showTray.Checked);
            if (!_showTray.Checked)
            {
                SetStatus("Icono oculto. Console Mode seguirá en segundo plano; ábrelo de nuevo para ver esta ventana.");
            }
        };
        _startWithWindows.CheckedChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }
            try
            {
                StartupManager.SetEnabled(_startWithWindows.Checked);
                SetStatus(_startWithWindows.Checked ? "Console Mode se iniciará con Windows." : "Console Mode ya no se iniciará con Windows.");
            }
            catch (Exception ex)
            {
                SetStatus($"No se pudo cambiar el inicio automático: {ex.Message}", error: true);
            }
        };
        _port.ValueChanged += (_, _) => SaveIfNotLoading(() => Settings.Port = (int)_port.Value);

        return page;
    }

    // =====================================================================================
    // Datos
    // =====================================================================================

    private void LoadValues()
    {
        _loading = true;
        try
        {
            LoadDeviceLists();
            _launchBigPicture.Checked = Settings.LaunchBigPicture;
            _moveBigPicture.Checked = Settings.MoveBigPictureToConsoleDisplay;
            _moveBigPicture.Enabled = Settings.LaunchBigPicture;
            _closeBigPicture.Checked = Settings.CloseBigPictureOnPcMode;
            _showTray.Checked = Settings.ShowTrayIcon;
            _startWithWindows.Checked = StartupManager.IsEnabled;
            _port.Value = Math.Clamp(Settings.Port, (int)_port.Minimum, (int)_port.Maximum);
        }
        finally
        {
            _loading = false;
        }
        RefreshQr();
        RefreshDevices();
    }

    private void LoadDeviceLists()
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            IReadOnlyList<DisplayInfo> displays;
            IReadOnlyList<AudioDeviceInfo> audio;
            try
            {
                displays = DisplayManager.GetDisplays();
            }
            catch (Exception ex)
            {
                displays = Array.Empty<DisplayInfo>();
                SetStatus($"No se pudieron leer las pantallas: {ex.Message}", error: true);
            }
            try
            {
                audio = AudioManager.GetOutputDevices();
            }
            catch (Exception ex)
            {
                audio = Array.Empty<AudioDeviceInfo>();
                SetStatus($"No se pudieron leer los dispositivos de audio: {ex.Message}", error: true);
            }

            var displayOptions = displays.Select(d => new Option(d.Id, d.Label, d.FriendlyName)).ToList();
            var audioOptions = audio.Select(a => new Option(a.Id, a.IsDefault ? $"{a.Name} · predeterminado" : a.Name, a.Name)).ToList();

            Fill(_pcDisplay, displayOptions, Settings.PcProfile.DisplayId, Settings.PcProfile.DisplayName, "no conectada");
            Fill(_consoleDisplay, displayOptions, Settings.ConsoleProfile.DisplayId, Settings.ConsoleProfile.DisplayName, "no conectada");
            Fill(_pcAudio, audioOptions, Settings.PcProfile.AudioDeviceId, Settings.PcProfile.AudioDeviceName, "no disponible");
            Fill(_consoleAudio, audioOptions, Settings.ConsoleProfile.AudioDeviceId, Settings.ConsoleProfile.AudioDeviceName, "no disponible");
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    private static void Fill(ComboBox combo, List<Option> options, string? selectedId, string? savedName, string missingText)
    {
        combo.BeginUpdate();
        combo.Items.Clear();
        combo.Items.Add(Option.NoChange);
        foreach (var o in options)
        {
            combo.Items.Add(o);
        }

        var selected = options.FirstOrDefault(o => o.Id == selectedId);
        if (selected == null && !string.IsNullOrEmpty(selectedId))
        {
            // El dispositivo guardado no está ahora mismo: lo mantenemos para no perder la configuración.
            selected = new Option(selectedId, $"{savedName ?? selectedId} ({missingText})", savedName ?? selectedId);
            combo.Items.Add(selected);
        }
        combo.SelectedItem = selected ?? Option.NoChange;
        combo.EndUpdate();
    }

    private void OnDisplayChanged(ComboBox combo, ModeProfile profile) => SaveIfNotLoading(() =>
    {
        var o = combo.SelectedItem as Option;
        profile.DisplayId = o?.Id;
        profile.DisplayName = o?.Name;
    });

    private void OnAudioChanged(ComboBox combo, ModeProfile profile) => SaveIfNotLoading(() =>
    {
        var o = combo.SelectedItem as Option;
        profile.AudioDeviceId = o?.Id;
        profile.AudioDeviceName = o?.Name;
    });

    private void SaveIfNotLoading(Action change)
    {
        if (_loading)
        {
            return;
        }
        lock (Settings)
        {
            change();
            Settings.Save();
        }
    }

    private void RefreshQr()
    {
        try
        {
            var uri = NetworkInfo.PairingUri(Settings);
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(8);
            var old = _qr.Image;
            _qr.Image = Image.FromStream(new MemoryStream(png));
            old?.Dispose();

            var ips = NetworkInfo.GetLocalAddresses();
            _addressLabel.Text = ips.Count == 0
                ? "No se ha detectado ninguna red."
                : $"{Environment.MachineName}\n{string.Join("\n", ips.Select(ip => $"{ip}:{Settings.Port}"))}";
        }
        catch (Exception ex)
        {
            _addressLabel.Text = $"No se pudo generar el QR: {ex.Message}";
        }
    }

    public void RefreshDevices()
    {
        List<TrustedDevice> devices;
        lock (Settings)
        {
            devices = Settings.TrustedDevices.ToList();
        }

        _devices.BeginUpdate();
        _devices.Items.Clear();
        foreach (var d in devices.OrderBy(d => d.Name))
        {
            var item = new ListViewItem(d.Name) { Tag = d.Id };
            item.SubItems.Add(d.AddedAt.ToString("g"));
            item.SubItems.Add(d.LastSeenAt?.ToString("g") ?? "—");
            _devices.Items.Add(item);
        }
        _devices.EndUpdate();
    }

    public void UpdateModeState(ModeState state)
    {
        _modeLabel.Text = state.Busy ? "Cambiando de modo…" : $"Modo actual: {state.Mode.ToDisplayName()}";
        StyleModeButton(_pcButton, state.Mode == AppMode.Pc, state.Busy);
        StyleModeButton(_consoleButton, state.Mode == AppMode.Console, state.Busy);

        if (state.Error != null)
        {
            SetStatus(state.Error, error: true);
        }
        else if (!state.Busy)
        {
            SetStatus($"Modo {state.Mode.ToDisplayName()} activado.");
            LoadDeviceLists(); // la pantalla y el audio predeterminados han cambiado
        }
    }

    private void SetStatus(string text, bool error = false)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = error ? Color.Firebrick : Color.DimGray;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Cerrar la ventana no cierra la app: sigue en segundo plano.
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    // =====================================================================================
    // Helpers de UI
    // =====================================================================================

    private sealed record Option(string? Id, string Label, string? Name)
    {
        public static readonly Option NoChange = new(null, "(No cambiar)", null);
        public override string ToString() => Label;
    }

    private static ComboBox NewCombo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill,
        Margin = new Padding(3, 3, 3, 6),
    };

    private static Button NewModeButton(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Height = 64,
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI Semibold", 13f),
        Margin = new Padding(4),
        Cursor = Cursors.Hand,
    };

    private static void StyleModeButton(Button button, bool active, bool busy)
    {
        // El modo activo también se puede pulsar: vuelve a aplicar su configuración.
        button.Enabled = !busy;
        button.BackColor = active ? Accent : Inactive;
        button.ForeColor = active ? Color.White : Color.FromArgb(40, 40, 48);
        button.FlatAppearance.BorderSize = 0;
    }

    private static GroupBox NewGroup(string title) => new()
    {
        Text = title,
        Dock = DockStyle.Top,
        AutoSize = true,
        Padding = new Padding(10, 6, 10, 8),
        Margin = new Padding(0, 0, 0, 10),
    };

    private static TableLayoutPanel NewGrid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private static void AddRow(TableLayoutPanel grid, string label, Control control)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 6) });
        grid.Controls.Add(control);
    }
}
