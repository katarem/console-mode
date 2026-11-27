using System.Diagnostics;
using ConsoleMode.Core;
using ConsoleMode.Native;
using ConsoleMode.Network;
using QRCoder;

namespace ConsoleMode.UI;

/// <summary>Ventana principal: interruptor PC/Consola, perfiles de cada modo, móviles y ajustes.</summary>
/// <remarks>
/// Sizes, margins and paddings are 96 DPI logical units: WinForms scales them (AutoScaleDimensions + AutoScaleMode.Dpi)
/// when the window is created and when it moves to a monitor with another scale. Texts wrap to the width of their
/// container (<see cref="WrapLabel"/>) instead of a fixed maximum width, and long explanations live in tooltips shown
/// from a «?» marker next to each option.
/// </remarks>
public sealed class MainForm : Form
{
    private static readonly Color Accent = Color.FromArgb(76, 70, 229);
    private static readonly Color Inactive = Color.FromArgb(230, 230, 236);
    private static readonly Size LogicalMinimumSize = new(560, 560);

    // Fonts derived from the form font (see ApplyDerivedFonts), so they follow it when the scale changes.
    private const string SemiboldFamily = "Segoe UI Semibold";
    private const float LargeFontFactor = 13f / 9.5f;

    private readonly TrayApplicationContext _app;
    private AppSettings Settings => _app.Settings;

    private readonly ToolTip _help = new() { AutoPopDelay = 30000, InitialDelay = 300, ReshowDelay = 100, ShowAlways = true };
    private readonly List<Control> _headings = new();
    private readonly List<Control> _helpMarkers = new();
    private Font? _largeFont;
    private Font? _semiboldFont;
    private Font? _boldFont;

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4) };
    private readonly Button _pcButton;
    private readonly Button _consoleButton;
    private readonly Label _modeLabel;
    private readonly WrapLabel _statusLabel = new() { ForeColor = Color.DimGray, Margin = new Padding(3, 4, 3, 0) };

    private readonly ComboBox _pcDisplay = NewCombo();
    private readonly ComboBox _pcAudio = NewCombo();
    private readonly ComboBox _consoleDisplay = NewCombo();
    private readonly ComboBox _consoleAudio = NewCombo();
    private readonly ComboBox _pcMic = NewCombo();
    private readonly ComboBox _consoleMic = NewCombo();
    private readonly ComboBox _pcComm = NewCombo();
    private readonly ComboBox _consoleComm = NewCombo();
    private readonly ComboBox _frontend = NewCombo();
    private readonly Label _frontendPathLabel = new() { Text = "Ruta:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 6) };
    private readonly TextBox _frontendPath = new() { Dock = DockStyle.Fill, Margin = new Padding(3, 3, 3, 6) };
    private readonly Button _frontendBrowse = new() { Text = "Examinar…", AutoSize = true, Margin = new Padding(3, 2, 3, 6) };
    private readonly Label _frontendArgsLabel = new() { Text = "Argumentos:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 6) };
    private readonly TextBox _frontendArgs = new() { Dock = DockStyle.Fill, Margin = new Padding(3, 3, 3, 6) };
    private readonly CheckBox _launchFrontend = new() { Text = "Abrirlo al entrar en modo consola", AutoSize = true };
    private readonly CheckBox _moveFrontend = new() { Text = "Forzarlo en la pantalla del modo consola", AutoSize = true };
    private readonly CheckBox _closeFrontend = new() { Text = "Cerrarlo al volver a modo PC", AutoSize = true };
    private readonly CheckBox _gamepadOverlay = new() { Text = "Abrir el overlay con View + Menú", AutoSize = true };
    private readonly CheckBox _minimizeExclusive = new() { Text = "Minimizar juegos en pantalla completa exclusiva", AutoSize = true };
    private readonly CheckBox _immersive = new() { Text = "Activar el modo consola inmersivo", AutoSize = true };
    private readonly Label _immersiveAllowedLabel = new() { Text = "Permitir también:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 6) };
    private readonly TextBox _immersiveAllowed = new()
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(3, 3, 3, 6),
        PlaceholderText = "Programas separados por comas (p. ej. Spotify)",
    };

    private readonly PictureBox _qr = new() { Size = new Size(200, 200), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White };
    private readonly WrapLabel _addressLabel = new() { Margin = new Padding(12, 0, 0, 0) };
    private readonly ListView _devices = new()
    {
        View = View.Details,
        FullRowSelect = true,
        MultiSelect = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        Dock = DockStyle.Fill,
    };

    private readonly CheckBox _showTray = new() { Text = "Icono en la bandeja del sistema", AutoSize = true };
    private readonly CheckBox _startWithWindows = new() { Text = "Iniciar con Windows (en segundo plano)", AutoSize = true };
    private readonly NumericUpDown _port = new() { Minimum = 1024, Maximum = 65535, Width = 90 };
    private readonly CheckBox _checkUpdates = new() { Text = "Buscar actualizaciones automáticamente", AutoSize = true };
    private readonly WrapLabel _updateLabel = new() { Margin = new Padding(3, 6, 3, 6) };
    private readonly Button _checkUpdatesButton = new() { Text = "Buscar actualizaciones", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _installUpdateButton = new() { Text = "Actualizar ahora", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Visible = false };

    private readonly CheckBox _wakeNetwork = new() { Text = "Activar Wake-on-LAN en la tarjeta de red", AutoSize = true };
    private readonly CheckBox _wakeGamepad = new() { Text = "Despertar el PC con el mando", AutoSize = true };
    private readonly CheckBox _wakeFastStartup = new() { Text = "Desactivar el inicio rápido de Windows", AutoSize = true };
    private readonly CheckBox _wakeNoSignIn = new() { Text = "No pedir el PIN al volver de la suspensión", AutoSize = true };
    private readonly Button _wakeApply = new() { Text = "Aplicar", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly Button _wakeRevert = new() { Text = "Deshacer todo", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
    private readonly WrapLabel _wakeStatus = new() { Margin = new Padding(3, 8, 3, 3) };
    private readonly Button _guideToggle = new() { Text = "Ver guía", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(3, 10, 3, 6) };

    private bool _loading;

    public MainForm(TrayApplicationContext app)
    {
        _app = app;
        SuspendLayout();

        Text = $"Console Mode {Application.ProductVersion.Split('+')[0]}";
        Icon = app.AppIcon;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 660);
        MinimumSize = LogicalMinimumSize;

        // ---------- Interruptor PC / Consola ----------
        _modeLabel = new Label { AutoSize = true, Margin = new Padding(4, 0, 0, 8) };
        _pcButton = NewModeButton("🖥  Modo PC");
        _consoleButton = NewModeButton("🎮  Modo Consola");
        _pcButton.Click += (_, _) => _ = _app.Switcher.SwitchAsync(AppMode.Pc);
        _consoleButton.Click += (_, _) => _ = _app.Switcher.SwitchAsync(AppMode.Console);

        var switchRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        switchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        switchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        switchRow.Controls.Add(_pcButton, 0, 0);
        switchRow.Controls.Add(_consoleButton, 1, 0);

        // ---------- Pestañas ----------
        _tabs.TabPages.Add(BuildModesTab());
        var devicesTab = BuildDevicesTab();
        _tabs.TabPages.Add(devicesTab);
        _tabs.TabPages.Add(BuildPowerTab());
        _tabs.TabPages.Add(BuildSettingsTab());
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_tabs.SelectedTab == devicesTab)
            {
                RefreshQr();
                RefreshDevices();
            }
        };

        // A single column whose rows take their height from their content; the tabs take the rest.
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12, 12, 12, 6) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(_modeLabel, 0, 0);
        root.Controls.Add(switchRow, 0, 1);
        root.Controls.Add(_tabs, 0, 2);
        root.Controls.Add(_statusLabel, 0, 3);
        Controls.Add(root);

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
        ApplyDpiState();

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
        var page = NewPage("Modos");
        var stack = NewStack();

        var pcGroup = NewGroup("🖥  Modo PC");
        pcGroup.Controls.Add(BuildProfileGrid(_pcDisplay, _pcAudio, _pcMic, _pcComm));
        var consoleGroup = NewGroup("🎮  Modo Consola");
        consoleGroup.Controls.Add(BuildProfileGrid(_consoleDisplay, _consoleAudio, _consoleMic, _consoleComm));

        var refresh = new Button { Text = "Actualizar pantallas y audio", AutoSize = true, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(3, 0, 3, 3) };
        refresh.Click += (_, _) =>
        {
            LoadDeviceLists();
            SetStatus("Lista de pantallas y dispositivos de audio actualizada.");
        };

        // ---------- Opciones del modo consola ----------
        var frontendGroup = NewGroup("Frontal");
        var frontendGrid = NewGrid();
        AddRow(frontendGrid, "Programa:", _frontend,
            "Lo que se abre al entrar en modo consola: Steam Big Picture, Playnite u otro programa (LaunchBox Big Box, " +
            "EmulationStation, un launcher propio...). Se abre en la pantalla principal.");
        var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(_frontendPath, 0, 0);
        pathRow.Controls.Add(_frontendBrowse, 1, 0);
        frontendGrid.Controls.Add(_frontendPathLabel);
        frontendGrid.Controls.Add(pathRow);
        frontendGrid.Controls.Add(_frontendArgsLabel);
        frontendGrid.Controls.Add(_frontendArgs);
        AddSpanning(frontendGrid, _launchFrontend);
        AddSpanning(frontendGrid, AddHelp(_moveFrontend,
            "Si el frontal se abre en otra pantalla (Steam a veces recuerda la última posición), Console Mode lo busca " +
            "durante un minuto y lo mueve y lo ajusta a la pantalla del modo consola."));
        AddSpanning(frontendGrid, _closeFrontend);
        frontendGroup.Controls.Add(frontendGrid);

        var overlayGroup = NewGroup("Overlay del mando");
        var overlayStack = NewStack(DockStyle.Fill);
        overlayStack.Controls.Add(AddHelp(_gamepadOverlay,
            "Mantén View + Menú medio segundo en un mando Xbox para cambiar de modo, de audio, de micrófono o de " +
            "pantalla, ajustar el volumen, cerrar el juego o apagar el PC desde el sofá."));
        overlayStack.Controls.Add(AddHelp(_minimizeExclusive,
            "Los juegos en pantalla completa exclusiva no dejan ver ninguna ventana encima: se minimizan al abrir el " +
            "overlay y se restauran al cerrarlo."));
        overlayGroup.Controls.Add(overlayStack);

        var immersiveGroup = NewGroup("Modo inmersivo");
        var immersiveGrid = NewGrid();
        AddSpanning(immersiveGrid, AddHelp(_immersive,
            "En modo consola solo se ven el frontal, los juegos y el overlay. Spotify, Discord y cualquier ventana que " +
            "no sea de un juego o de una tienda se ocultan en cuanto aparecen y vuelven a verse al pasar a modo PC."));
        immersiveGrid.Controls.Add(_immersiveAllowedLabel);
        immersiveGrid.Controls.Add(_immersiveAllowed);
        immersiveGroup.Controls.Add(immersiveGrid);

        stack.Controls.Add(pcGroup);
        stack.Controls.Add(consoleGroup);
        stack.Controls.Add(AddHelp(refresh, "Vuelve a leer las pantallas y los dispositivos de audio conectados."));
        stack.Controls.Add(Heading("Opciones del modo consola"));
        stack.Controls.Add(frontendGroup);
        stack.Controls.Add(overlayGroup);
        stack.Controls.Add(immersiveGroup);
        page.Controls.Add(stack);

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

    /// <summary>Pantalla y audio de un modo.</summary>
    private TableLayoutPanel BuildProfileGrid(ComboBox display, ComboBox audio, ComboBox mic, ComboBox comm)
    {
        var grid = NewGrid();
        AddRow(grid, "Pantalla principal:", display,
            "Pantalla que pasa a ser la principal en este modo. «(No cambiar)» la deja como esté.");
        AddRow(grid, "Salida de audio:", audio,
            "Salida de audio predeterminada en este modo. «(No cambiar)» la deja como esté.");
        AddRow(grid, "Micrófono:", mic,
            "Micrófono predeterminado en este modo. Se usa para todo, también para comunicaciones.");
        AddRow(grid, "Comunicaciones:", comm,
            "Salida que usan Discord, Teams o Zoom con el dispositivo de comunicaciones predeterminado: así puedes oír " +
            "el juego en la TV y el chat en los auriculares.");
        return grid;
    }

    private TabPage BuildDevicesTab()
    {
        var page = new TabPage("Móviles") { Padding = new Padding(10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var right = NewStack(DockStyle.Fill);
        right.Controls.Add(new WrapLabel
        {
            Text = "Escanea el código con la app Console Mode del móvil.",
            Margin = new Padding(12, 0, 0, 4),
        });
        right.Controls.Add(new WrapLabel
        {
            Text = "O búscalo desde la app con «Buscar en la red».",
            Margin = new Padding(12, 0, 0, 8),
        });
        right.Controls.Add(new WrapLabel
        {
            Text = "La primera vez tendrás que aceptar el móvil en este PC.",
            ForeColor = Color.DimGray,
            Margin = new Padding(12, 0, 0, 12),
        });
        right.Controls.Add(_addressLabel);

        _qr.Margin = new Padding(3, 3, 3, 10);
        layout.Controls.Add(_qr, 0, 0);
        layout.Controls.Add(right, 1, 0);

        var devicesGroup = NewGroup("Dispositivos de confianza");
        devicesGroup.AutoSize = false;
        _devices.Columns.Add("Nombre");
        _devices.Columns.Add("Añadido");
        _devices.Columns.Add("Última conexión");
        _devices.Resize += (_, _) => FitDeviceColumns();

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
        var page = NewPage("Encendido");
        var stack = NewStack();

        stack.Controls.Add(Paragraph(
            "Enciende o despierta este PC desde el móvil, o con el mando si tu hardware lo permite. Console Mode prepara " +
            "Windows; tú solo tienes que activar Wake-on-LAN en la BIOS (mira la guía)."));

        var setup = NewGroup("Preparar Windows");
        var setupStack = NewStack(DockStyle.Fill);
        setupStack.Controls.Add(AddHelp(_wakeNetwork,
            "Solo con el «paquete mágico» que envía el móvil: el PC no se enciende con cualquier tráfico de la red."));
        setupStack.Controls.Add(AddHelp(_wakeGamepad,
            "Permite que el adaptador Xbox o el Bluetooth despierten el PC de la suspensión. Desde apagado no es posible."));
        setupStack.Controls.Add(AddHelp(_wakeFastStartup,
            "En muchos PCs hace falta para poder encenderlo desde apagado."));
        setupStack.Controls.Add(AddHelp(_wakeNoSignIn,
            "Al volver de la suspensión no se pide el PIN. Al encender o reiniciar se sigue pidiendo."));
        var setupButtons = NewButtonRow();
        setupButtons.Margin = new Padding(0, 8, 0, 0);
        setupButtons.Controls.Add(_wakeApply);
        setupButtons.Controls.Add(_wakeRevert);
        setupStack.Controls.Add(AddHelp(setupButtons,
            "Windows pedirá permiso de administrador. Console Mode guarda cómo estaba cada ajuste: con «Deshacer todo», " +
            "o al desinstalar la app, todo vuelve a quedar como antes. Al aplicar, la red puede cortarse un momento."));
        setupStack.Controls.Add(_wakeStatus);
        setup.Controls.Add(setupStack);
        stack.Controls.Add(setup);

        var guide = NewGroup("Guía");
        guide.Visible = false;
        var guideStack = NewStack(DockStyle.Fill);
        guideStack.Controls.Add(Heading("1. Activa Wake-on-LAN en la BIOS"));
        guideStack.Controls.Add(Paragraph(
            "Reinicia el PC y entra en la BIOS/UEFI (normalmente con Supr o F2 al arrancar). Según el fabricante, la " +
            "opción se llama «Wake on LAN», «Power On By PCI-E», «Resume by PCI-E Device» o «Remote Wake Up» y suele " +
            "estar en Energía o APM. Si existe «ErP» o «Deep Sleep», desactívalo: deja la tarjeta de red sin corriente " +
            "con el PC apagado."));
        guideStack.Controls.Add(Heading("2. Conecta el PC por cable"));
        guideStack.Controls.Add(Paragraph(
            "Wake-on-LAN funciona de forma fiable por Ethernet. Por Wi-Fi casi nunca enciende el PC apagado, y desde " +
            "suspensión solo en algunos equipos."));
        guideStack.Controls.Add(Heading("3. Pulsa «Aplicar» y abre la app del móvil"));
        guideStack.Controls.Add(Paragraph(
            "Conecta el móvil a este PC al menos una vez con la versión nueva de la app: así guarda los datos para " +
            "encenderlo. Después, con el PC apagado o en suspensión, aparece el botón «Encender el PC»."));
        guideStack.Controls.Add(Heading("Con el mando"));
        guideStack.Controls.Add(Paragraph(
            "El mando solo puede despertar el PC de la suspensión, y depende del adaptador y de la placa base: pruébalo " +
            "pulsando el botón Xbox con el PC suspendido. Desde apagado no es posible."));
        guideStack.Controls.Add(Heading("Entrar sin escribir la contraseña"));
        guideStack.Controls.Add(Paragraph(
            "Si quieres que Windows inicie sesión solo al encender, configúralo con las herramientas de Windows: Console " +
            "Mode no guarda tu contraseña. En «Cuentas de usuario» (netplwiz), desmarca «Los usuarios deben escribir su " +
            "nombre y contraseña». Si no aparece esa casilla, en Configuración → Cuentas → Opciones de inicio de sesión " +
            "desactiva «Para mayor seguridad, permitir solo el inicio de sesión de Windows Hello». Para deshacerlo, " +
            "vuelve a marcar la casilla."));
        var guideButtons = NewButtonRow();
        var netplwiz = new Button { Text = "Abrir «Cuentas de usuario»", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        netplwiz.Click += (_, _) => OpenExternal("netplwiz.exe");
        var signInOptions = new Button { Text = "Abrir opciones de inicio de sesión", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        signInOptions.Click += (_, _) => OpenExternal("ms-settings:signinoptions");
        guideButtons.Controls.Add(netplwiz);
        guideButtons.Controls.Add(signInOptions);
        guideStack.Controls.Add(guideButtons);
        guide.Controls.Add(guideStack);

        _guideToggle.Click += (_, _) =>
        {
            guide.Visible = !guide.Visible;
            _guideToggle.Text = guide.Visible ? "Ocultar guía" : "Ver guía";
        };
        stack.Controls.Add(_guideToggle);
        stack.Controls.Add(guide);
        page.Controls.Add(stack);

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

    private Label Heading(string text)
    {
        var heading = new Label { Text = text, AutoSize = true, Margin = new Padding(3, 10, 3, 4) };
        _headings.Add(heading);
        return heading;
    }

    private static WrapLabel Paragraph(string text) => new()
    {
        Text = text,
        ForeColor = Color.DimGray,
        Margin = new Padding(3, 2, 3, 6),
    };

    private TabPage BuildSettingsTab()
    {
        var page = NewPage("Ajustes");
        var stack = NewStack();

        var portRow = NewButtonRow();
        portRow.WrapContents = false;
        portRow.Margin = new Padding(0, 8, 0, 8);
        portRow.Controls.Add(new Label { Text = "Puerto de escucha:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) });
        portRow.Controls.Add(AddHelp(_port, "Puerto en el que el móvil se conecta a este PC. Se aplica al reiniciar la app."));

        var buttons = NewButtonRow();
        buttons.Margin = new Padding(0, 4, 0, 0);
        var openFolder = new Button { Text = "Abrir carpeta de configuración", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        openFolder.Click += (_, _) => Process.Start(new ProcessStartInfo(AppSettings.Directory) { UseShellExecute = true });
        var exit = new Button { Text = "Salir de Console Mode", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
        exit.Click += (_, _) => _app.Exit();
        buttons.Controls.Add(openFolder);
        buttons.Controls.Add(exit);

        stack.Controls.Add(AddHelp(_showTray,
            "El icono aparece en «iconos ocultos». Console Mode sigue funcionando en segundo plano al cerrar esta " +
            "ventana; si ocultas el icono, vuelve a abrir Console Mode (menú Inicio o el .exe) para verla."));
        stack.Controls.Add(AddHelp(_startWithWindows,
            "Arranca al iniciar sesión, directamente en segundo plano, sin abrir esta ventana."));
        stack.Controls.Add(portRow);
        stack.Controls.Add(BuildUpdatesGroup());
        stack.Controls.Add(buttons);
        page.Controls.Add(stack);

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
        var group = NewGroup("Actualizaciones");
        group.Margin = new Padding(0, 4, 0, 8);
        var stack = NewStack(DockStyle.Fill);
        var buttons = NewButtonRow();
        buttons.Controls.Add(_checkUpdatesButton);
        buttons.Controls.Add(_installUpdateButton);
        var edition = Updater.IsInstalled ? "instalada" : "portable";
        stack.Controls.Add(new WrapLabel
        {
            Text = $"Versión {Updater.CurrentVersion} · {edition}",
            Margin = new Padding(3, 3, 3, 3),
        });
        stack.Controls.Add(_updateLabel);
        stack.Controls.Add(buttons);
        stack.Controls.Add(_checkUpdates);
        group.Controls.Add(stack);

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
                _frontendPath.PlaceholderText = "Ruta del .exe (Big Box, EmulationStation, tu launcher…)";
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

    /// <summary>
    /// The immersive console mode was switched somewhere else (the gamepad overlay): shows it without saving it again.
    /// </summary>
    public void ShowImmersiveState(bool enabled)
    {
        if (IsDisposed)
        {
            return;
        }
        var wasLoading = _loading;
        _loading = true;
        try
        {
            _immersive.Checked = enabled;
            _immersiveAllowedLabel.Enabled = _immersiveAllowed.Enabled = enabled;
        }
        finally
        {
            _loading = wasLoading;
        }
    }

    // =====================================================================================
    // Escalado (DPI)
    // =====================================================================================

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyDpiState();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        // WinForms rescales the form font and the layout inside base.OnDpiChanged; what it does not rescale is set
        // again once it has finished.
        BeginInvoke(new Action(ApplyDpiState));
    }

    /// <summary>Fonts and sizes that depend on the current scale. Idempotent: everything is computed from scratch.</summary>
    private void ApplyDpiState()
    {
        if (IsDisposed)
        {
            return;
        }
        ApplyDerivedFonts();
        MinimumSize = LogicalToDeviceUnits(LogicalMinimumSize);
        _tabs.Padding = new Point(LogicalToDeviceUnits(12), LogicalToDeviceUnits(6));
        FitDeviceColumns();
    }

    /// <summary>Larger and bold fonts, derived from the form font so they keep the proportion at every scale.</summary>
    private void ApplyDerivedFonts()
    {
        var size = Font.SizeInPoints;
        var large = new Font(SemiboldFamily, size * LargeFontFactor);
        var semibold = new Font(SemiboldFamily, size);
        var bold = new Font(Font, FontStyle.Bold);

        _modeLabel.Font = large;
        _pcButton.Font = large;
        _consoleButton.Font = large;
        foreach (var heading in _headings)
        {
            heading.Font = semibold;
        }
        foreach (var marker in _helpMarkers)
        {
            marker.Font = bold;
        }
        _addressLabel.Font = bold;

        _largeFont?.Dispose();
        _semiboldFont?.Dispose();
        _boldFont?.Dispose();
        (_largeFont, _semiboldFont, _boldFont) = (large, semibold, bold);
    }

    /// <summary>Column widths of the device list as a share of its width, so no column is cut at any scale.</summary>
    private void FitDeviceColumns()
    {
        var width = _devices.ClientSize.Width;
        if (_devices.Columns.Count < 3 || width <= 0)
        {
            return;
        }
        _devices.Columns[0].Width = width * 46 / 100;
        _devices.Columns[1].Width = width * 27 / 100;
        _devices.Columns[2].Width = width - _devices.Columns[0].Width - _devices.Columns[1].Width;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _help.Dispose();
            _largeFont?.Dispose();
            _semiboldFont?.Dispose();
            _boldFont?.Dispose();
        }
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
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Padding = new Padding(8, 14, 8, 14),
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
        Dock = DockStyle.Fill,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Padding = new Padding(10, 6, 10, 8),
        Margin = new Padding(0, 0, 0, 10),
    };

    private static TabPage NewPage(string title) => new(title) { Padding = new Padding(10), AutoScroll = true };

    /// <summary>
    /// Vertical stack as wide as its container. Unlike a top-down FlowLayoutPanel, its children can stretch to that
    /// width, so their texts wrap instead of being cut or adding a horizontal scroll bar.
    /// </summary>
    private static TableLayoutPanel NewStack(DockStyle dock = DockStyle.Top)
    {
        var stack = new TableLayoutPanel
        {
            Dock = dock,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Margin = Padding.Empty,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return stack;
    }

    /// <summary>Row of buttons that moves the ones that do not fit to the next line.</summary>
    private static FlowLayoutPanel NewButtonRow() => new()
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = true,
        Margin = Padding.Empty,
    };

    private static TableLayoutPanel NewGrid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    private void AddRow(TableLayoutPanel grid, string label, Control control, string? help = null)
    {
        var caption = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 8, 6) };
        if (help == null)
        {
            grid.Controls.Add(caption);
        }
        else
        {
            caption.Margin = new Padding(3, 6, 2, 6);
            var captionWithHelp = AddHelp(caption, help);
            captionWithHelp.Margin = new Padding(0, 0, 6, 0);
            grid.Controls.Add(captionWithHelp);
            _help.SetToolTip(control, UiText.WrapForTooltip(help));
        }
        grid.Controls.Add(control);
    }

    private static void AddSpanning(TableLayoutPanel grid, Control control)
    {
        grid.Controls.Add(control);
        grid.SetColumnSpan(control, grid.ColumnCount);
    }

    /// <summary>
    /// Puts <paramref name="target"/> next to a «?» marker that shows <paramref name="help"/> as a tooltip (also when
    /// clicked) and returns both in a row to add to the layout instead of the target.
    /// </summary>
    private Control AddHelp(Control target, string help)
    {
        var text = UiText.WrapForTooltip(help);
        _help.SetToolTip(target, text);
        target.AccessibleDescription = help;
        target.Anchor = AnchorStyles.Left;

        var marker = new Label
        {
            Text = "?",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            ForeColor = Accent,
            Cursor = Cursors.Help,
            Margin = new Padding(0, 0, 3, 0),
            AccessibleName = "Ayuda",
            AccessibleDescription = help,
        };
        _help.SetToolTip(marker, text);
        marker.Click += (_, _) => _help.Show(text, marker, 0, marker.Height, 15000);
        _helpMarkers.Add(marker);

        var row = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 1,
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(target, 0, 0);
        row.Controls.Add(marker, 1, 0);
        return row;
    }
}
