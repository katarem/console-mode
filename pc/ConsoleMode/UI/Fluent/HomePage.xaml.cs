using System.Windows;
using ConsoleMode.Core;
using Wpf.Ui.Controls;

namespace ConsoleMode.UI.Fluent;

/// <summary>Home: switch between PC and console mode, and shortcuts to the overlay, the phones and remote power.</summary>
public partial class HomePage : System.Windows.Controls.Page
{
    private readonly TrayApplicationContext _app;
    private readonly MainWindow _window;

    public HomePage(TrayApplicationContext app, MainWindow window)
    {
        _app = app;
        _window = window;
        InitializeComponent();
        RefreshOverlay();
        RefreshDeviceCount();
    }

    public void UpdateModeState(ModeState state)
    {
        PcButton.Appearance = state.Mode == AppMode.Pc ? ControlAppearance.Primary : ControlAppearance.Secondary;
        ConsoleButton.Appearance = state.Mode == AppMode.Console ? ControlAppearance.Primary : ControlAppearance.Secondary;
        // The active mode can be pressed too: it applies its settings again.
        PcButton.IsEnabled = ConsoleButton.IsEnabled = !state.Busy;
        BusyPanel.Visibility = state.Busy ? Visibility.Visible : Visibility.Collapsed;
    }

    public void RefreshOverlay()
    {
        var settings = _app.Settings;
        OverlayCombo.Family = settings.ButtonIcons;
        OverlayCombo.Buttons = settings.OverlayComboButtons;
        OverlayState.Text = settings.GamepadOverlayEnabled ? "Activado" : "Desactivado";
        OverlayState.SetResourceReference(TextBlock.ForegroundProperty,
            settings.GamepadOverlayEnabled ? "SystemFillColorSuccessBrush" : "TextFillColorSecondaryBrush");
    }

    public void RefreshDeviceCount()
    {
        int count;
        lock (_app.Settings)
        {
            count = _app.Settings.TrustedDevices.Count;
        }
        DeviceCount.Text = count switch
        {
            0 => "Ninguno vinculado",
            1 => "1 vinculado",
            _ => $"{count} vinculados",
        };
    }

    private void OnPcClick(object sender, RoutedEventArgs e) => _ = _app.Switcher.SwitchAsync(AppMode.Pc);

    private void OnConsoleClick(object sender, RoutedEventArgs e) => _ = _app.Switcher.SwitchAsync(AppMode.Console);

    private async void OnChangeComboClick(object sender, RoutedEventArgs e) => await ComboCaptureDialog.ShowAsync(_app, _window);

    private void OnPairClick(object sender, RoutedEventArgs e) => _window.Navigate(typeof(DevicesPage));

    private void OnPowerClick(object sender, RoutedEventArgs e) => _window.Navigate(typeof(PowerPage));
}
