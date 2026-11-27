using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConsoleMode.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ConsoleMode.UI.Fluent;

/// <summary>Main window: navigation on the left and one page per section. Closing it only hides it.</summary>
public partial class MainWindow : FluentWindow, IMainWindow
{
    private readonly HomePage _home;
    private readonly ModesPage _modes;
    private readonly ConsoleModePage _console;
    private readonly DevicesPage _devices;
    private readonly PowerPage _power;
    private readonly SettingsPage _settings;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _exiting;

    public MainWindow(TrayApplicationContext app)
    {
        InitializeComponent();

        Title = $"Console Mode {System.Windows.Forms.Application.ProductVersion.Split('+')[0]}";
        AppTitleBar.Title = Title;
        var icon = Imaging.CreateBitmapSourceFromHIcon(app.AppIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        Icon = icon;
        AppTitleBar.Icon = new ImageIcon { Source = icon };

        _home = new HomePage(app, this);
        _modes = new ModesPage(app, this);
        _console = new ConsoleModePage(app, this);
        _devices = new DevicesPage(app, this);
        _power = new PowerPage(this);
        _settings = new SettingsPage(app, this);
        Navigation.SetPageProviderService(new PageProvider(new object[] { _home, _modes, _console, _devices, _power, _settings }));
        Loaded += (_, _) => Navigation.Navigate(typeof(HomePage));

        // Follows the Windows light/dark theme, also while the window is open; the accent stays Console Mode's violet.
        SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: false);

        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusBar.IsOpen = false;
        };

        _home.UpdateModeState(app.Switcher.State); // the modes page has just read the devices
        if (app.ServerError != null)
        {
            ShowStatus(app.ServerError, error: true);
        }
    }

    internal ContentDialogHost Dialogs => DialogHost;

    /// <summary>Message at the top of the window. Information closes by itself after a few seconds; errors stay.</summary>
    internal void ShowStatus(string text, bool error = false)
    {
        _statusTimer.Stop();
        StatusBar.Message = text;
        StatusBar.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        StatusBar.IsOpen = true;
        if (!error)
        {
            _statusTimer.Start();
        }
    }

    internal void Navigate(Type page) => Navigation.Navigate(page);

    /// <summary>The overlay combination or the button icons changed: every page that draws them redraws.</summary>
    internal void OverlaySettingsChanged()
    {
        _home.RefreshOverlay();
        _console.RefreshOverlay();
    }

    /// <summary>The number of trusted devices changed.</summary>
    internal void DeviceCountChanged() => _home.RefreshDeviceCount();

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    public void RefreshDevices()
    {
        _devices.RefreshDevices();
        _home.RefreshDeviceCount();
    }

    public void RefreshDeviceLists() => _modes.RefreshDeviceLists();

    public void UpdateModeState(ModeState state)
    {
        ApplyModeState(state);
        if (state.Error != null)
        {
            ShowStatus(state.Error, error: true);
        }
        else if (!state.Busy)
        {
            ShowStatus($"Modo {state.Mode.ToDisplayName()} activado.");
        }
    }

    public void ShowImmersiveState(bool enabled) => _console.ShowImmersiveState(enabled);

    public void CloseForExit()
    {
        _exiting = true;
        _settings.Detach();
        _statusTimer.Stop();
        Close();
    }

    private void ApplyModeState(ModeState state)
    {
        _home.UpdateModeState(state);
        _modes.UpdateModeState(state);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window does not close the app: it keeps running in the background.
        if (!_exiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}
