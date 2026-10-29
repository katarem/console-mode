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
    private readonly ComboBox _pcMic = NewCombo();
    private readonly ComboBox _consoleMic = NewCombo();
    private readonly ComboBox _pcComm = NewCombo();
    private readonly ComboBox _consoleComm = NewCombo();
    private readonly ComboBox _frontend = NewCombo();
    private readonly Label _frontendPathLabel = new() { Text = "Programa:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 6) };
    private readonly TextBox _frontendPath = new() { Dock = DockStyle.Fill, Margin = new Padding(3, 3, 3, 6) };
    private readonly Button _frontendBrowse = new() { Text = "Examinar…", AutoSize = true, Margin = new Padding(3, 2, 3, 6) };
    private readonly Label _frontendArgsLabel = new() { Text = "Argumentos:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 6) };
    private readonly TextBox _frontendArgs = new() { Dock = DockStyle.Fill, Margin = new Padding(3, 3, 3, 6) };
    private readonly CheckBox _launchFrontend = new() { Text = "Abrir el frontal al activar el modo consola", AutoSize = true };
    private readonly CheckBox _moveFrontend = new() { Text = "Forzar el frontal en la pantalla del modo consola", AutoSize = true };
    private readonly CheckBox _closeFrontend = new() { Text = "Cerrar el frontal al volver al modo PC", AutoSize = true };
    private readonly CheckBox _minimizeExclusive = new()
    {
        Text = "Con juegos en pantalla completa exclusiva, minimizarlos para ver el overlay (y restaurarlos al cerrarlo)",
        AutoSize = true,
        MaximumSize = new Size(520, 0),
        Margin = new Padding(22, 0, 3, 3),
    };
    private readonly CheckBox _immersive = new() { Text = "Modo consola inmersivo: solo se ven el frontal, los juegos y el overlay", AutoSize = true };
    private readonly Label _immersiveHint = new()
    {
        Text = "Spotify, Discord y cualquier ventana que no sea de un juego o de una tienda se ocultan en cuanto aparecen " +
               "y vuelven a verse al pasar a modo PC.",
        AutoSize = true,
        MaximumSize = new Size(500, 0),
        ForeColor = Color.DimGray,
        Margin = new Padding(22, 0, 3, 3),
    };
    private readonly Label _immersiveAllowedLabel = new() { Text = "Permitir también:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(22, 6, 8, 6) };
    private readonly TextBox _immersiveAllowed = new()
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(3, 3, 3, 6),
        PlaceholderText = "Nombres de programa separados por comas (p. ej. Spotify)",
    };
    private readonly CheckBox _gamepadOverlay = new()
    {
        Text = "Overlay con el mando: mantén View + Menú 1 s para cambiar audio y pantalla",
        AutoSize = true,
    };

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
    private readonly CheckBox _checkUpdates = new() { Text = "Buscar actualizaciones automáticamente", AutoSize = true };
    private readonly Label _updateLabel = new() { AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(3, 6, 3, 6) };
    private readonly Button _checkUpdatesButton = new() { Text = "Buscar actualizaciones", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _installUpdateButton = new() { Text = "Actualizar ahora", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Visible = false };

    private readonly CheckBox _wakeNetwork = new()
    {
        Text = "Tarjeta de red: activar Wake-on-LAN (solo con el «paquete mágico» que envía el móvil)",
        AutoSize = true,
        MaximumSize = new Size(540, 0),
    };
    private readonly CheckBox _wakeGamepad = new()
    {
        Text = "Mando: permitir que el adaptador Xbox o el Bluetooth despierten el PC de la suspensión",
        AutoSize = true,
        MaximumSize = new Size(540, 0),
    };
    private readonly CheckBox _wakeFastStartup = new()
    {
        Text = "Desactivar el inicio rápido de Windows (en muchos PCs hace falta para encender desde apagado)",
        AutoSize = true,
        MaximumSize = new Size(540, 0),
    };
    private readonly CheckBox _wakeNoSignIn = new()
    {
        Text = "No pedir el PIN al volver de la suspensión (al encender o reiniciar se sigue pidiendo)",
        AutoSize = true,
        MaximumSize = new Size(540, 0),
    };
    private readonly Button _wakeApply = new() { Text = "Aplicar", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _wakeRevert = new() { Text = "Deshacer todo", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Label _wakeStatus = new() { AutoSize = true, MaximumSize = new Size(540, 0), Margin = new Padding(3, 8, 3, 3) };

    private bool _loading;

    public MainForm(TrayApplicationContext app)
    {
        _app = app;

        Text = $"Console Mode {Application.ProductVersion.Split('+')[0]}";
        Icon = app.AppIcon;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 660);
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
        tabs.TabPages.Add(BuildPowerTab());
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
        AddRow(pcGrid, "Micrófono:", _pcMic);
        AddRow(pcGrid, "Comunicaciones:", _pcComm);
        pcGroup.Controls.Add(pcGrid);

        var consoleGroup = NewGroup("🎮  Modo Consola");
        var consoleGrid = NewGrid();
        AddRow(consoleGrid, "Pantalla principal:", _consoleDisplay);
        AddRow(consoleGrid, "Salida de audio:", _consoleAudio);
        AddRow(consoleGrid, "Micrófono:", _consoleMic);
        AddRow(consoleGrid, "Comunicaciones:", _consoleComm);
        AddRow(consoleGrid, "Frontal:", _frontend);
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(_frontendPath, 0, 0);
        pathRow.Controls.Add(_frontendBrowse, 1, 0);
        consoleGrid.Controls.Add(_frontendPathLabel);
        consoleGrid.Controls.Add(pathRow);
        consoleGrid.Controls.Add(_frontendArgsLabel);
        consoleGrid.Controls.Add(_frontendArgs);
        consoleGrid.Controls.Add(_launchFrontend);
        consoleGrid.SetColumnSpan(_launchFrontend, 2);
        consoleGrid.Controls.Add(_moveFrontend);
        consoleGrid.SetColumnSpan(_moveFrontend, 2);
        consoleGrid.Controls.Add(_closeFrontend);
        consoleGrid.SetColumnSpan(_closeFrontend, 2);
        consoleGrid.Controls.Add(_gamepadOverlay);
        consoleGrid.SetColumnSpan(_gamepadOverlay, 2);
        consoleGrid.Controls.Add(_minimizeExclusive);
        consoleGrid.SetColumnSpan(_minimizeExclusive, 2);
        consoleGrid.Controls.Add(_immersive);
        consoleGrid.SetColumnSpan(_immersive, 2);
        consoleGrid.Controls.Add(_immersiveHint);
        consoleGrid.SetColumnSpan(_immersiveHint, 2);
        consoleGrid.Controls.Add(_immersiveAllowedLabel);
        consoleGrid.Controls.Add(_immersiveAllowed);
        consoleGroup.Controls.Add(consoleGrid);

        var refresh = new Button { Text = "Actualizar pantallas y audio", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        refresh.Click += (_, _) =>
        {
            LoadDeviceLists();
            SetStatus("Lista de pantallas y dispositivos de audio actualizada.");
        };

        var hint = new Label
        {
            Text = "«(No cambiar)» deja la pantalla o el audio como estén. «Comunicaciones» es la salida que usan Discord, " +
                   "Teams o Zoom con el dispositivo de comunicaciones predeterminado: así puedes oír el juego en la TV y el chat " +
                   "en los auriculares. El micrófono se usa para todo, también para comunicaciones. El frontal (Big Picture, " +
                   "Playnite u otro programa, como LaunchBox Big Box o EmulationStation) se abre en la pantalla principal; si " +
                   "se abre en otra, Console Mode lo mueve a la del modo consola.",
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
        _pcMic.SelectedIndexChanged += (_, _) => OnComboChanged(_pcMic, (o, p) => (p.MicrophoneId, p.MicrophoneName) = (o?.Id, o?.Name), Settings.PcProfile);
        _consoleMic.SelectedIndexChanged += (_, _) => OnComboChanged(_consoleMic, (o, p) => (p.MicrophoneId, p.MicrophoneName) = (o?.Id, o?.Name), Settings.ConsoleProfile);
        _pcComm.SelectedIndexChanged += (_, _) => OnComboChanged(_pcComm, (o, p) => (p.CommunicationsId, p.CommunicationsName) = (o?.Id, o?.Name), Settings.PcProfile);
        _consoleComm.SelectedIndexChanged += (_, _) => OnComboChanged(_consoleComm, (o, p) => (p.CommunicationsId, p.CommunicationsName) = (o?.Id, o?.Name), Settings.ConsoleProfile);
        _frontend.Items.AddRange(new object[] { "Steam Big Picture", "Playnite (pantalla completa)", "Otro programa…" });
        _frontend.SelectedIndexChanged += (_, _) =>
        {
            SaveIfNotLoading(() => Settings.Frontend = (FrontendKind)_frontend.SelectedIndex);
            ShowFrontendOptions();
        };
        _frontendPath.TextChanged += (_, _) => SaveIfNotLoading(() =>
        {
            var path = string.IsNullOrWhiteSpace(_frontendPath.Text) ? null : _frontendPath.Text.Trim().Trim('"');
            if (Settings.Frontend == FrontendKind.Playnite)
            {
                Settings.PlaynitePath = path;
            }
            else
            {
                Settings.CustomFrontendPath = path;
            }
        });
        _frontendArgs.TextChanged += (_, _) => SaveIfNotLoading(() =>
            Settings.CustomFrontendArgs = string.IsNullOrWhiteSpace(_frontendArgs.Text) ? null : _frontendArgs.Text.Trim());
        _frontendBrowse.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog
            {
                Title = Settings.Frontend == FrontendKind.Playnite ? $"Elige {ConsoleFrontend.PlayniteExe}" : "Elige el programa del frontal",
                Filter = "Programas (*.exe)|*.exe",
                FileName = Settings.Frontend == FrontendKind.Playnite ? ConsoleFrontend.PlayniteExe : "",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _frontendPath.Text = dialog.FileName;
            }
        };
        _launchFrontend.CheckedChanged += (_, _) => SaveIfNotLoading(() =>
        {
            Settings.LaunchFrontend = _launchFrontend.Checked;
            _moveFrontend.Enabled = _launchFrontend.Checked;
        });
        _moveFrontend.CheckedChanged += (_, _) => SaveIfNotLoading(() => Settings.MoveFrontendToConsoleDisplay = _moveFrontend.Checked);
        _closeFrontend.CheckedChanged += (_, _) => SaveIfNotLoading(() => Settings.CloseFrontendOnPcMode = _closeFrontend.Checked);
        _minimizeExclusive.CheckedChanged += (_, _) => SaveIfNotLoading(() => Settings.MinimizeExclusiveFullscreen = _minimizeExclusive.Checked);
        _immersive.CheckedChanged += (_, _) =>
        {
            _immersiveAllowedLabel.Enabled = _immersiveAllowed.Enabled = _immersive.Checked;
            if (!_loading)
            {
                _app.SetImmersiveConsole(_immersive.Checked);
            }
        };
        _immersiveAllowed.TextChanged += (_, _) => SaveIfNotLoading(() =>
            Settings.ImmersiveAllowedPrograms = _immersiveAllowed.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
                .ToList());
        _gamepadOverlay.CheckedChanged += (_, _) =>
        {
            if (!_loading)
            {
                _app.SetGamepadOverlayEnabled(_gamepadOverlay.Checked);
            }
        };

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

    private TabPage BuildPowerTab()
    {
        var page = new TabPage("Encendido") { Padding = new Padding(10), AutoScroll = true };
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };

        layout.Controls.Add(Paragraph(
            "Enciende o despierta este PC desde el móvil con Wake-on-LAN, y despiértalo con el mando si tu hardware lo " +
            "permite. Console Mode puede preparar Windows por ti; lo único que tienes que hacer a mano es activar " +
            "Wake-on-LAN en la BIOS (mira la guía de abajo)."));

        var setup = NewGroup("Preparar Windows");
        setup.Dock = DockStyle.None;
        setup.MinimumSize = new Size(540, 0);
        var setupFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        setupFlow.Controls.Add(_wakeNetwork);
        setupFlow.Controls.Add(_wakeGamepad);
        setupFlow.Controls.Add(_wakeFastStartup);
        setupFlow.Controls.Add(_wakeNoSignIn);
        var setupButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        setupButtons.Controls.Add(_wakeApply);
        setupButtons.Controls.Add(_wakeRevert);
        setupFlow.Controls.Add(setupButtons);
        setupFlow.Controls.Add(Paragraph(
            "Windows pedirá permiso de administrador. Console Mode guarda cómo estaba cada ajuste: con «Deshacer todo», " +
            "o al desinstalar la app, todo vuelve a quedar como antes. Al aplicar, la red puede cortarse un momento."));
        setupFlow.Controls.Add(_wakeStatus);
        setup.Controls.Add(setupFlow);
        layout.Controls.Add(setup);

        var guide = NewGroup("Guía");
        guide.Dock = DockStyle.None;
        guide.MinimumSize = new Size(540, 0);
        var guideFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        guideFlow.Controls.Add(Heading("1. Activa Wake-on-LAN en la BIOS"));
        guideFlow.Controls.Add(Paragraph(
            "Reinicia el PC y entra en la BIOS/UEFI (normalmente con Supr o F2 al arrancar). Según el fabricante, la " +
            "opción se llama «Wake on LAN», «Power On By PCI-E», «Resume by PCI-E Device» o «Remote Wake Up» y suele " +
            "estar en Energía o APM. Si existe «ErP» o «Deep Sleep», desactívalo: deja la tarjeta de red sin corriente " +
            "con el PC apagado."));
        guideFlow.Controls.Add(Heading("2. Conecta el PC por cable"));
        guideFlow.Controls.Add(Paragraph(
            "Wake-on-LAN funciona de forma fiable por Ethernet. Por Wi-Fi casi nunca enciende el PC apagado, y desde " +
            "suspensión solo en algunos equipos."));
        guideFlow.Controls.Add(Heading("3. Pulsa «Aplicar» y abre la app del móvil"));
        guideFlow.Controls.Add(Paragraph(
            "Conecta el móvil a este PC al menos una vez con la versión nueva de la app: así guarda los datos para " +
            "encenderlo. Después, con el PC apagado o en suspensión, aparece el botón «Encender el PC»."));
        guideFlow.Controls.Add(Heading("Con el mando"));
        guideFlow.Controls.Add(Paragraph(
            "El mando solo puede despertar el PC de la suspensión, y depende del adaptador y de la placa base: pruébalo " +
            "pulsando el botón Xbox con el PC suspendido. Desde apagado no es posible."));
        guideFlow.Controls.Add(Heading("Entrar sin escribir la contraseña"));
        guideFlow.Controls.Add(Paragraph(
            "Si quieres que Windows inicie sesión solo al encender, configúralo con las herramientas de Windows: Console " +
            "Mode no guarda tu contraseña. En «Cuentas de usuario» (netplwiz), desmarca «Los usuarios deben escribir su " +
            "nombre y contraseña». Si no aparece esa casilla, en Configuración → Cuentas → Opciones de inicio de sesión " +
            "desactiva «Para mayor seguridad, permitir solo el inicio de sesión de Windows Hello». Para deshacerlo, " +
            "vuelve a marcar la casilla."));
        var guideButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var netplwiz = new Button { Text = "Abrir «Cuentas de usuario»", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        netplwiz.Click += (_, _) => OpenExternal("netplwiz.exe");
        var signInOptions = new Button { Text = "Abrir opciones de inicio de sesión", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        signInOptions.Click += (_, _) => OpenExternal("ms-settings:signinoptions");
        guideButtons.Controls.Add(netplwiz);
        guideButtons.Controls.Add(signInOptions);
        guideFlow.Controls.Add(guideButtons);
        guide.Controls.Add(guideFlow);
        layout.Controls.Add(guide);

        page.Controls.Add(layout);

        _wakeApply.Click += async (_, _) =>
        {
            var options = new WakeSetupOptions(_wakeNetwork.Checked, _wakeGamepad.Checked, _wakeFastStartup.Checked, _wakeNoSignIn.Checked);
            if (options.ToArg().Length == 0)
            {
                await RunWakeSetupAsync("Restaurando los ajustes de Windows…", WakeSetup.RevertElevatedAsync);
                return;
            }
            await RunWakeSetupAsync("Preparando Windows…", () => WakeSetup.ApplyElevatedAsync(options));
        };
        _wakeRevert.Click += async (_, _) =>
            await RunWakeSetupAsync("Restaurando los ajustes de Windows…", WakeSetup.RevertElevatedAsync);

        return page;
    }

    private async Task RunWakeSetupAsync(string progressText, Func<Task<bool>> action)
    {
        _wakeApply.Enabled = _wakeRevert.Enabled = false;
        _wakeStatus.ForeColor = Color.DimGray;
        _wakeStatus.Text = progressText;
        try
        {
            if (!await action())
            {
                ShowWakeState(note: "Cancelado: no se ha cambiado nada.");
                return;
            }
            ShowWakeState();
        }
        catch (Exception ex)
        {
            Log.Error("Encendido a distancia", ex);
            ShowWakeState(note: ex.Message, error: true);
        }
        finally
        {
            _wakeApply.Enabled = true;
        }
    }

    /// <summary>Marca las opciones aplicadas y muestra el resultado de cada paso.</summary>
    private void ShowWakeState(string? note = null, bool error = false)
    {
        var state = WakeSetup.LoadState();
        var options = state?.Options ?? WakeSetupOptions.Default;
        var wasLoading = _loading;
        _loading = true;
        _wakeNetwork.Checked = options.Network;
        _wakeGamepad.Checked = options.Gamepad;
        _wakeFastStartup.Checked = options.DisableFastStartup;
        _wakeNoSignIn.Checked = options.NoSignInOnWake;
        _loading = wasLoading;
        _wakeRevert.Enabled = state != null;

        var lines = new List<string>();
        if (note != null)
        {
            lines.Add(note);
        }
        if (state == null)
        {
            lines.Add("Windows está como siempre: Console Mode no ha cambiado nada.");
        }
        else
        {
            lines.Add($"Aplicado el {state.AppliedAt:g}:");
            lines.AddRange(state.Results.Select(r => $"{(r.Ok ? "✔" : "⚠")}  {r.Message}"));
        }
        _wakeStatus.Text = string.Join("\n", lines);
        _wakeStatus.ForeColor = error ? Color.Firebrick : Color.DimGray;
    }

    private void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // Aviso de administrador cancelado.
        }
        catch (Exception ex)
        {
            SetStatus($"No se pudo abrir: {ex.Message}", error: true);
        }
    }

    private static Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(530, 0),
        Font = new Font("Segoe UI Semibold", 9.5f),
        Margin = new Padding(3, 8, 3, 2),
    };

    private static Label Paragraph(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(530, 0),
        ForeColor = Color.DimGray,
        Margin = new Padding(3, 2, 3, 6),
    };

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
        layout.Controls.Add(BuildUpdatesGroup());
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

    private GroupBox BuildUpdatesGroup()
    {
        var group = new GroupBox
        {
            Text = "Actualizaciones",
            AutoSize = true,
            Padding = new Padding(10, 6, 10, 8),
            Margin = new Padding(0, 4, 0, 8),
            MinimumSize = new Size(540, 0),
        };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        buttons.Controls.Add(_checkUpdatesButton);
        buttons.Controls.Add(_installUpdateButton);
        flow.Controls.Add(new Label
        {
            Text = $"Versión {Updater.CurrentVersion} · {(Updater.IsInstalled ? "instalada" : "portable")}",
            AutoSize = true,
            Margin = new Padding(3, 3, 3, 3),
        });
        flow.Controls.Add(_updateLabel);
        flow.Controls.Add(buttons);
        flow.Controls.Add(_checkUpdates);
        group.Controls.Add(flow);

        _checkUpdates.CheckedChanged += (_, _) => SaveIfNotLoading(() => Settings.CheckForUpdates = _checkUpdates.Checked);
        _checkUpdatesButton.Click += async (_, _) =>
        {
            _checkUpdatesButton.Enabled = false;
            _updateLabel.Text = "Buscando…";
            try
            {
                var update = await _app.CheckForUpdatesAsync(manual: true);
                if (update == null)
                {
                    _updateLabel.Text = "Tienes la última versión.";
                }
            }
            catch (Exception ex)
            {
                _updateLabel.Text = $"No se pudo comprobar: {ex.Message}";
            }
            finally
            {
                _checkUpdatesButton.Enabled = true;
            }
        };
        _installUpdateButton.Click += async (_, _) =>
        {
            if (_app.AvailableUpdate is not { } update)
            {
                return;
            }
            _installUpdateButton.Enabled = false;
            _checkUpdatesButton.Enabled = false;
            try
            {
                var progress = new Progress<int>(p => _updateLabel.Text = $"Descargando la versión {update.Version}… {p} %");
                await _app.InstallUpdateAsync(update, progress);
                // Si seguimos aquí es que se canceló el aviso de administrador del instalador.
                _updateLabel.Text = "Actualización cancelada.";
            }
            catch (Exception ex)
            {
                _updateLabel.Text = $"No se pudo actualizar: {ex.Message}";
                Log.Error("Error al actualizar", ex);
            }
            finally
            {
                _installUpdateButton.Enabled = true;
                _checkUpdatesButton.Enabled = true;
            }
        };
        _app.UpdateStateChanged += ShowUpdateState;
        Disposed += (_, _) => _app.UpdateStateChanged -= ShowUpdateState;
        ShowUpdateState();
        return group;
    }

    private void ShowUpdateState()
    {
        if (IsDisposed)
        {
            return;
        }
        var update = _app.AvailableUpdate;
        _installUpdateButton.Visible = update != null;
        if (update != null)
        {
            _updateLabel.Text = $"Hay una versión nueva: {update.Version}. " +
                                (Updater.IsInstalled
                                    ? "Se instalará con el instalador (pedirá permiso de administrador) y la app se volverá a abrir."
                                    : "Se sustituirá este .exe y la app se volverá a abrir.");
        }
        else if (string.IsNullOrEmpty(_updateLabel.Text))
        {
            _updateLabel.Text = "Sin comprobar todavía.";
        }
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
            _frontend.SelectedIndex = (int)Settings.Frontend;
            ShowFrontendOptions();
            _launchFrontend.Checked = Settings.LaunchFrontend;
            _moveFrontend.Checked = Settings.MoveFrontendToConsoleDisplay;
            _moveFrontend.Enabled = Settings.LaunchFrontend;
            _closeFrontend.Checked = Settings.CloseFrontendOnPcMode;
            _gamepadOverlay.Checked = Settings.GamepadOverlayEnabled;
            _minimizeExclusive.Checked = Settings.MinimizeExclusiveFullscreen;
            _immersive.Checked = Settings.ImmersiveConsole;
            _immersiveAllowedLabel.Enabled = _immersiveAllowed.Enabled = Settings.ImmersiveConsole;
            _immersiveAllowed.Text = string.Join(", ", Settings.ImmersiveAllowedPrograms);
            _showTray.Checked = Settings.ShowTrayIcon;
            _startWithWindows.Checked = StartupManager.IsEnabled;
            _port.Value = Math.Clamp(Settings.Port, (int)_port.Minimum, (int)_port.Maximum);
            _checkUpdates.Checked = Settings.CheckForUpdates;
        }
        finally
        {
            _loading = false;
        }
        ShowWakeState();
        RefreshQr();
        RefreshDevices();
    }

    /// <summary>Ruta del programa (Playnite u otro) y argumentos (solo otro programa) según el frontal elegido.</summary>
    private void ShowFrontendOptions()
    {
        var kind = Settings.Frontend;
        var wasLoading = _loading;
        _loading = true;
        try
        {
            _frontendPathLabel.Visible = _frontendPath.Parent!.Visible = kind != FrontendKind.SteamBigPicture;
            _frontendArgsLabel.Visible = _frontendArgs.Visible = kind == FrontendKind.Custom;
            if (kind == FrontendKind.Playnite)
            {
                _frontendPath.Text = Settings.PlaynitePath ?? "";
                var detected = ConsoleFrontend.DetectPlaynite();
                _frontendPath.PlaceholderText = detected ?? $"No se ha encontrado Playnite: instálalo o elige {ConsoleFrontend.PlayniteExe}";
            }
            else if (kind == FrontendKind.Custom)
            {
                _frontendPath.Text = Settings.CustomFrontendPath ?? "";
                _frontendPath.PlaceholderText = "Ruta del .exe (LaunchBox Big Box, EmulationStation, tu launcher…)";
                _frontendArgs.Text = Settings.CustomFrontendArgs ?? "";
            }
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    /// <summary>Se ha conectado o quitado una pantalla o un dispositivo de audio.</summary>
    public void RefreshDeviceLists()
    {
        if (!IsDisposed)
        {
            LoadDeviceLists();
        }
    }

    private void LoadDeviceLists()
    {
        var wasLoading = _loading;
        _loading = true;
        try
        {
            IReadOnlyList<DisplayInfo> displays;
            IReadOnlyList<AudioDeviceInfo> audio;
            IReadOnlyList<AudioDeviceInfo> mics;
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
            try
            {
                mics = AudioManager.GetInputDevices();
            }
            catch (Exception ex)
            {
                mics = Array.Empty<AudioDeviceInfo>();
                SetStatus($"No se pudieron leer los micrófonos: {ex.Message}", error: true);
            }

            var displayOptions = displays.Select(d => new Option(d.Id, d.Label, d.FriendlyName)).ToList();
            var audioOptions = audio.Select(a => new Option(a.Id, a.IsDefault ? $"{a.Name} · predeterminado" : a.Name, a.Name)).ToList();

            Fill(_pcDisplay, displayOptions, Settings.PcProfile.DisplayId, Settings.PcProfile.DisplayName, "no conectada");
            Fill(_consoleDisplay, displayOptions, Settings.ConsoleProfile.DisplayId, Settings.ConsoleProfile.DisplayName, "no conectada");
            Fill(_pcAudio, audioOptions, Settings.PcProfile.AudioDeviceId, Settings.PcProfile.AudioDeviceName, "no disponible");
            Fill(_consoleAudio, audioOptions, Settings.ConsoleProfile.AudioDeviceId, Settings.ConsoleProfile.AudioDeviceName, "no disponible");
            var micOptions = mics.Select(a => new Option(a.Id, a.IsDefault ? $"{a.Name} · predeterminado" : a.Name, a.Name)).ToList();
            var commOptions = audio.Select(a => new Option(a.Id, a.Name, a.Name)).ToList();
            Fill(_pcMic, micOptions, Settings.PcProfile.MicrophoneId, Settings.PcProfile.MicrophoneName, "no disponible");
            Fill(_consoleMic, micOptions, Settings.ConsoleProfile.MicrophoneId, Settings.ConsoleProfile.MicrophoneName, "no disponible");
            Fill(_pcComm, commOptions, Settings.PcProfile.CommunicationsId, Settings.PcProfile.CommunicationsName, "no disponible");
            Fill(_consoleComm, commOptions, Settings.ConsoleProfile.CommunicationsId, Settings.ConsoleProfile.CommunicationsName, "no disponible");
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

    private void OnComboChanged(ComboBox combo, Action<Option?, ModeProfile> apply, ModeProfile profile) =>
        SaveIfNotLoading(() => apply(combo.SelectedItem as Option, profile));

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
