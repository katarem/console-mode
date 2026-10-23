using System.Reflection;
using ConsoleMode.Core;
using ConsoleMode.Network;

namespace ConsoleMode.UI;

/// <summary>
/// Núcleo de la app: vive en segundo plano, con o sin icono en la bandeja,
/// y es dueño de la ventana principal, el servidor y el cambio de modo.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Control _ui;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _pcItem;
    private readonly ToolStripMenuItem _consoleItem;
    private readonly EventWaitHandle _showEvent;
    private readonly RegisteredWaitHandle _showWait;
    private readonly EventWaitHandle _exitEvent;
    private readonly RegisteredWaitHandle _exitWait;
    private MainForm? _form;

    public TrayApplicationContext(bool showWindow)
    {
        // Control invisible para volver al hilo de UI desde los hilos de red.
        _ui = new Control();
        _ = _ui.Handle;

        Settings = AppSettings.Load();
        StartupManager.RefreshPathIfEnabled();
        AppIcon = LoadIcon();

        Switcher = new ModeSwitcher(Settings);
        Switcher.StateChanged += state => RunOnUi(() => OnStateChanged(state));

        Server = new RemoteServer(Settings, Switcher, ConfirmPairingAsync);
        Server.DevicesChanged += () => RunOnUi(() => _form?.RefreshDevices());
        Server.RemoteModeRequested += (device, mode) =>
            RunOnUi(() => Notify($"{device} ha pedido el modo {mode.ToDisplayName()}"));
        try
        {
            Server.Start();
        }
        catch (Exception ex)
        {
            ServerError = $"No se pudo abrir el puerto {Settings.Port}: {ex.Message}";
            Log.Error(ServerError, ex);
        }

        Discovery = new DiscoveryResponder(Settings);
        try
        {
            Discovery.Start();
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir el puerto de descubrimiento {AppSettings.DiscoveryPort}", ex);
        }

        _pcItem = new ToolStripMenuItem("Modo PC", null, (_, _) => _ = Switcher.SwitchAsync(AppMode.Pc));
        _consoleItem = new ToolStripMenuItem("Modo Consola", null, (_, _) => _ = Switcher.SwitchAsync(AppMode.Console));
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Abrir Console Mode", null, (_, _) => ShowMainWindow()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pcItem);
        menu.Items.Add(_consoleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Salir", null, (_, _) => Exit()));

        _tray = new NotifyIcon
        {
            Icon = AppIcon,
            ContextMenuStrip = menu,
            Visible = Settings.ShowTrayIcon,
        };
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainWindow();
            }
        };
        UpdateTray(Switcher.State);

        // Si alguien vuelve a ejecutar el .exe, la segunda instancia nos pide mostrar la ventana.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => RunOnUi(ShowMainWindow), null, -1, false);
        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitEvent, (_, _) => RunOnUi(Exit), null, -1, true);

        if (showWindow)
        {
            ShowMainWindow();
        }
    }

    public AppSettings Settings { get; }
    public ModeSwitcher Switcher { get; }
    public RemoteServer Server { get; }
    public DiscoveryResponder Discovery { get; }
    public Icon AppIcon { get; }
    public string? ServerError { get; }

    public void ShowMainWindow()
    {
        if (_form == null || _form.IsDisposed)
        {
            _form = new MainForm(this);
        }
        if (!_form.Visible)
        {
            _form.Show();
        }
        if (_form.WindowState == FormWindowState.Minimized)
        {
            _form.WindowState = FormWindowState.Normal;
        }
        _form.Activate();
        _form.BringToFront();
    }

    public void SetTrayIconVisible(bool visible)
    {
        lock (Settings)
        {
            Settings.ShowTrayIcon = visible;
            Settings.Save();
        }
        _tray.Visible = visible;
    }

    public void RemoveTrustedDevice(string deviceId)
    {
        lock (Settings)
        {
            Settings.TrustedDevices.RemoveAll(d => d.Id == deviceId);
            Settings.Save();
        }
        Server.DisconnectDevice(deviceId);
    }

    public void Exit()
    {
        _showWait.Unregister(null);
        _exitWait.Unregister(null);
        _tray.Visible = false;
        Server.Dispose();
        Discovery.Dispose();
        _form?.Close();
        _form?.Dispose();
        ExitThread();
    }

    private void OnStateChanged(ModeState state)
    {
        UpdateTray(state);
        _form?.UpdateModeState(state);
        if (state.Error != null)
        {
            Notify($"Cambio a modo {state.Mode.ToDisplayName()} con avisos: {state.Error}", ToolTipIcon.Warning);
        }
    }

    private void UpdateTray(ModeState state)
    {
        _pcItem.Checked = state.Mode == AppMode.Pc;
        _consoleItem.Checked = state.Mode == AppMode.Console;
        _pcItem.Enabled = _consoleItem.Enabled = !state.Busy;
        _tray.Text = state.Busy ? "Console Mode · cambiando…" : $"Console Mode · modo {state.Mode.ToDisplayName()}";
    }

    private void Notify(string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_tray.Visible)
        {
            _tray.ShowBalloonTip(4000, "Console Mode", text, icon);
        }
    }

    /// <summary>Muestra al usuario del PC la petición de un móvil nuevo y espera su respuesta.</summary>
    private Task<bool> ConfirmPairingAsync(PairRequest request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        RunOnUi(() =>
        {
            var dialog = new PairRequestForm(request, AppIcon);
            var registration = ct.Register(() => RunOnUi(() =>
            {
                if (!dialog.IsDisposed)
                {
                    dialog.Close();
                }
            }));
            dialog.FormClosed += (_, _) =>
            {
                registration.Dispose();
                tcs.TrySetResult(dialog.Accepted);
                dialog.Dispose();
            };
            dialog.Show();
            dialog.Activate();
        });
        return tcs.Task;
    }

    private void RunOnUi(Action action)
    {
        if (_ui.IsDisposed)
        {
            return;
        }
        if (_ui.InvokeRequired)
        {
            _ui.BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private static Icon LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ConsoleMode.app.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tray.Dispose();
            _showEvent.Dispose();
            _exitEvent.Dispose();
            _ui.Dispose();
        }
        base.Dispose(disposing);
    }
}
