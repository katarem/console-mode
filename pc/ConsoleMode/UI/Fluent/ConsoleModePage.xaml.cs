using System.Windows;
using System.Windows.Controls;
using ConsoleMode.Core;
using ConsoleMode.Native;

namespace ConsoleMode.UI.Fluent;

/// <summary>Console mode options: the gamepad overlay, the frontend opened on the TV and the immersive mode.</summary>
public partial class ConsoleModePage : Page
{
    private readonly TrayApplicationContext _app;
    private readonly MainWindow _window;
    private readonly SettingsEditor _editor;

    private AppSettings Settings => _app.Settings;

    public ConsoleModePage(TrayApplicationContext app, MainWindow window)
    {
        _app = app;
        _window = window;
        _editor = new SettingsEditor(app.Settings);
        InitializeComponent();

        FrontendCombo.ItemsSource = new[] { "Steam Big Picture", "Playnite (pantalla completa)", "Otro programa…" };
        using (_editor.BeginLoading())
        {
            OverlaySwitch.IsChecked = Settings.GamepadOverlayEnabled;
            MinimizeExclusiveSwitch.IsChecked = Settings.MinimizeExclusiveFullscreen;
            FrontendCombo.SelectedIndex = (int)Settings.Frontend;
            LaunchSwitch.IsChecked = Settings.LaunchFrontend;
            MoveSwitch.IsChecked = Settings.MoveFrontendToConsoleDisplay;
            MoveSwitch.IsEnabled = Settings.LaunchFrontend;
            CloseSwitch.IsChecked = Settings.CloseFrontendOnPcMode;
            ImmersiveSwitch.IsChecked = Settings.ImmersiveConsole;
            ImmersiveAllowed.IsEnabled = Settings.ImmersiveConsole;
            ImmersiveAllowed.Text = string.Join(", ", Settings.ImmersiveAllowedPrograms);
            ShowFrontendOptions();
            RefreshOverlay();
        }

        OverlaySwitch.Checked += (_, _) => OnOverlaySwitched();
        OverlaySwitch.Unchecked += (_, _) => OnOverlaySwitched();
        MinimizeExclusiveSwitch.Click += (_, _) => _editor.Save(() =>
            Settings.MinimizeExclusiveFullscreen = MinimizeExclusiveSwitch.IsChecked == true);
        FrontendCombo.SelectionChanged += (_, _) =>
        {
            _editor.Save(() => Settings.Frontend = (FrontendKind)FrontendCombo.SelectedIndex);
            ShowFrontendOptions();
        };
        FrontendPath.TextChanged += (_, _) => _editor.Save(() =>
        {
            var path = string.IsNullOrWhiteSpace(FrontendPath.Text) ? null : FrontendPath.Text.Trim().Trim('"');
            if (Settings.Frontend == FrontendKind.Playnite)
            {
                Settings.PlaynitePath = path;
            }
            else
            {
                Settings.CustomFrontendPath = path;
            }
        });
        FrontendArgs.TextChanged += (_, _) => _editor.Save(() =>
            Settings.CustomFrontendArgs = string.IsNullOrWhiteSpace(FrontendArgs.Text) ? null : FrontendArgs.Text.Trim());
        LaunchSwitch.Click += (_, _) => _editor.Save(() =>
        {
            Settings.LaunchFrontend = LaunchSwitch.IsChecked == true;
            MoveSwitch.IsEnabled = LaunchSwitch.IsChecked == true;
        });
        MoveSwitch.Click += (_, _) => _editor.Save(() => Settings.MoveFrontendToConsoleDisplay = MoveSwitch.IsChecked == true);
        CloseSwitch.Click += (_, _) => _editor.Save(() => Settings.CloseFrontendOnPcMode = CloseSwitch.IsChecked == true);
        ImmersiveSwitch.Click += (_, _) =>
        {
            var enabled = ImmersiveSwitch.IsChecked == true;
            ImmersiveAllowed.IsEnabled = enabled;
            _app.SetImmersiveConsole(enabled);
        };
        ImmersiveAllowed.TextChanged += (_, _) => _editor.Save(() =>
            Settings.ImmersiveAllowedPrograms = ImmersiveAllowed.Text
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name)
                .ToList());
    }

    /// <summary>The combination or the button icons changed.</summary>
    public void RefreshOverlay()
    {
        using var loading = _editor.BeginLoading();
        var family = Settings.ButtonIcons;
        OverlayCombo.Family = family;
        OverlayCombo.Buttons = Settings.OverlayComboButtons;
        HintPreview.Family = family;
        (family switch
        {
            ButtonIconFamily.PlayStation => PlayStationIcons,
            ButtonIconFamily.Nintendo => NintendoIcons,
            _ => XboxIcons,
        }).IsChecked = true;
    }

    /// <summary>The immersive mode was switched from the overlay: shows it without saving it again.</summary>
    public void ShowImmersiveState(bool enabled)
    {
        ImmersiveSwitch.IsChecked = enabled;
        ImmersiveAllowed.IsEnabled = enabled;
    }

    private void OnOverlaySwitched()
    {
        if (_editor.IsLoading)
        {
            return;
        }
        _app.SetGamepadOverlayEnabled(OverlaySwitch.IsChecked == true);
        _window.OverlaySettingsChanged();
    }

    private void OnIconsChecked(object sender, RoutedEventArgs e)
    {
        if (_editor.IsLoading)
        {
            return;
        }
        var family = PlayStationIcons.IsChecked == true ? ButtonIconFamily.PlayStation
            : NintendoIcons.IsChecked == true ? ButtonIconFamily.Nintendo
            : ButtonIconFamily.Xbox;
        _app.SetButtonIconFamily(family);
        _window.OverlaySettingsChanged();
    }

    private async void OnChangeComboClick(object sender, RoutedEventArgs e) => await ComboCaptureDialog.ShowAsync(_app, _window);

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var playnite = Settings.Frontend == FrontendKind.Playnite;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = playnite ? $"Elige {ConsoleFrontend.PlayniteExe}" : "Elige el programa del frontal",
            Filter = "Programas (*.exe)|*.exe",
            FileName = playnite ? ConsoleFrontend.PlayniteExe : "",
        };
        if (dialog.ShowDialog(_window) == true)
        {
            FrontendPath.Text = dialog.FileName;
        }
    }

    /// <summary>Path of the program (Playnite or another one) and arguments (another one only) for the chosen frontend.</summary>
    private void ShowFrontendOptions()
    {
        using var loading = _editor.BeginLoading();
        var kind = Settings.Frontend;
        PathRow.Visibility = kind != FrontendKind.SteamBigPicture ? Visibility.Visible : Visibility.Collapsed;
        FrontendArgs.Visibility = kind == FrontendKind.Custom ? Visibility.Visible : Visibility.Collapsed;
        if (kind == FrontendKind.Playnite)
        {
            FrontendPath.Text = Settings.PlaynitePath ?? "";
            FrontendPath.PlaceholderText = ConsoleFrontend.DetectPlaynite()
                                           ?? $"No se ha encontrado Playnite: instálalo o elige {ConsoleFrontend.PlayniteExe}";
        }
        else if (kind == FrontendKind.Custom)
        {
            FrontendPath.Text = Settings.CustomFrontendPath ?? "";
            FrontendPath.PlaceholderText = "Ruta del .exe (Big Box, EmulationStation, tu launcher…)";
            FrontendArgs.Text = Settings.CustomFrontendArgs ?? "";
        }
    }
}
