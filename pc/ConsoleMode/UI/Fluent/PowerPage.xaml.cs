using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ConsoleMode.Core;

namespace ConsoleMode.UI.Fluent;

/// <summary>Prepares Windows to be woken up from the phone (Wake-on-LAN) or with the gamepad.</summary>
public partial class PowerPage : Page
{
    private readonly MainWindow _window;

    public PowerPage(MainWindow window)
    {
        _window = window;
        InitializeComponent();
        ShowWakeState();
    }

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var options = new WakeSetupOptions(WakeNetwork.IsChecked == true, WakeGamepad.IsChecked == true,
            WakeFastStartup.IsChecked == true, WakeNoSignIn.IsChecked == true);
        if (options.ToArg().Length == 0)
        {
            await RunWakeSetupAsync("Restaurando los ajustes de Windows…", WakeSetup.RevertElevatedAsync);
            return;
        }
        await RunWakeSetupAsync("Preparando Windows…", () => WakeSetup.ApplyElevatedAsync(options));
    }

    private async void OnRevertClick(object sender, RoutedEventArgs e) =>
        await RunWakeSetupAsync("Restaurando los ajustes de Windows…", WakeSetup.RevertElevatedAsync);

    private async Task RunWakeSetupAsync(string progressText, Func<Task<bool>> action)
    {
        ApplyButton.IsEnabled = RevertButton.IsEnabled = false;
        SetWakeStatus(progressText, error: false);
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
            ApplyButton.IsEnabled = true;
        }
    }

    /// <summary>Ticks the options that are applied and shows the result of each step.</summary>
    private void ShowWakeState(string? note = null, bool error = false)
    {
        var state = WakeSetup.LoadState();
        var options = state?.Options ?? WakeSetupOptions.Default;
        WakeNetwork.IsChecked = options.Network;
        WakeGamepad.IsChecked = options.Gamepad;
        WakeFastStartup.IsChecked = options.DisableFastStartup;
        WakeNoSignIn.IsChecked = options.NoSignInOnWake;
        RevertButton.IsEnabled = state != null;

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
        SetWakeStatus(string.Join("\n", lines), error);
    }

    private void SetWakeStatus(string text, bool error)
    {
        WakeStatus.Text = text;
        WakeStatus.SetResourceReference(TextBlock.ForegroundProperty, error ? "SystemFillColorCriticalBrush" : "TextFillColorSecondaryBrush");
    }

    private void OnNetplwizClick(object sender, RoutedEventArgs e) => OpenExternal("netplwiz.exe");

    private void OnSignInOptionsClick(object sender, RoutedEventArgs e) => OpenExternal("ms-settings:signinoptions");

    private void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // The administrator prompt was cancelled.
        }
        catch (Exception ex)
        {
            _window.ShowStatus($"No se pudo abrir: {ex.Message}", error: true);
        }
    }
}
