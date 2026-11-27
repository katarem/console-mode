using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ConsoleMode.Core;

namespace ConsoleMode.UI.Fluent;

/// <summary>Tray icon, start with Windows, listening port and updates.</summary>
public partial class SettingsPage : Page
{
    private readonly TrayApplicationContext _app;
    private readonly MainWindow _window;
    private readonly SettingsEditor _editor;

    private AppSettings Settings => _app.Settings;

    public SettingsPage(TrayApplicationContext app, MainWindow window)
    {
        _app = app;
        _window = window;
        _editor = new SettingsEditor(app.Settings);
        InitializeComponent();

        using (_editor.BeginLoading())
        {
            TraySwitch.IsChecked = Settings.ShowTrayIcon;
            StartupSwitch.IsChecked = StartupManager.IsEnabled;
            PortBox.Value = Math.Clamp(Settings.Port, 1024, 65535);
            AutoUpdateSwitch.IsChecked = Settings.CheckForUpdates;
            VersionText.Text = $"Versión {Updater.CurrentVersion} · {(Updater.IsInstalled ? "instalada" : "portable")}";
        }

        TraySwitch.Click += (_, _) =>
        {
            var visible = TraySwitch.IsChecked == true;
            _app.SetTrayIconVisible(visible);
            if (!visible)
            {
                _window.ShowStatus("Icono oculto. Console Mode seguirá en segundo plano; ábrelo de nuevo para ver esta ventana.");
            }
        };
        StartupSwitch.Click += (_, _) =>
        {
            try
            {
                StartupManager.SetEnabled(StartupSwitch.IsChecked == true);
                _window.ShowStatus(StartupSwitch.IsChecked == true
                    ? "Console Mode se iniciará con Windows."
                    : "Console Mode ya no se iniciará con Windows.");
            }
            catch (Exception ex)
            {
                _window.ShowStatus($"No se pudo cambiar el inicio automático: {ex.Message}", error: true);
            }
        };
        PortBox.ValueChanged += (_, _) =>
        {
            if (PortBox.Value is { } value && value is >= 1024 and <= 65535)
            {
                _editor.Save(() => Settings.Port = (int)value);
            }
        };
        AutoUpdateSwitch.Click += (_, _) => _editor.Save(() => Settings.CheckForUpdates = AutoUpdateSwitch.IsChecked == true);

        _app.UpdateStateChanged += ShowUpdateState;
        ShowUpdateState();
    }

    /// <summary>Stops listening to the app (the window is closing for good).</summary>
    public void Detach() => _app.UpdateStateChanged -= ShowUpdateState;

    private void ShowUpdateState()
    {
        var update = _app.AvailableUpdate;
        InstallButton.Visibility = update != null ? Visibility.Visible : Visibility.Collapsed;
        if (update != null)
        {
            UpdateText.Text = $"Hay una versión nueva: {update.Version}. " +
                              (Updater.IsInstalled
                                  ? "Se instalará con el instalador (pedirá permiso de administrador) y la app se volverá a abrir."
                                  : "Se sustituirá este .exe y la app se volverá a abrir.");
        }
        else if (string.IsNullOrEmpty(UpdateText.Text))
        {
            UpdateText.Text = "Sin comprobar todavía.";
        }
    }

    private async void OnCheckClick(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        UpdateText.Text = "Buscando…";
        try
        {
            var update = await _app.CheckForUpdatesAsync(manual: true);
            if (update == null)
            {
                UpdateText.Text = "Tienes la última versión.";
            }
        }
        catch (Exception ex)
        {
            UpdateText.Text = $"No se pudo comprobar: {ex.Message}";
        }
        finally
        {
            CheckButton.IsEnabled = true;
        }
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (_app.AvailableUpdate is not { } update)
        {
            return;
        }
        InstallButton.IsEnabled = CheckButton.IsEnabled = false;
        try
        {
            var progress = new Progress<int>(p => UpdateText.Text = $"Descargando la versión {update.Version}… {p} %");
            await _app.InstallUpdateAsync(update, progress);
            // Still here: the installer's administrator prompt was cancelled.
            UpdateText.Text = "Actualización cancelada.";
        }
        catch (Exception ex)
        {
            UpdateText.Text = $"No se pudo actualizar: {ex.Message}";
            Log.Error("Update failed", ex);
        }
        finally
        {
            InstallButton.IsEnabled = CheckButton.IsEnabled = true;
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(AppSettings.Directory) { UseShellExecute = true });

    private void OnExitClick(object sender, RoutedEventArgs e) => _app.Exit();
}
