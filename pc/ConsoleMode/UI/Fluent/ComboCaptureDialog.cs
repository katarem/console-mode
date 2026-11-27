using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ConsoleMode.Core;
using Wpf.Ui.Controls;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBlock = System.Windows.Controls.TextBlock;

namespace ConsoleMode.UI.Fluent;

/// <summary>
/// «Cambiar» on the overlay combination: the user holds the buttons they want on the gamepad and saves them. The
/// overlay cannot open while the dialog is shown.
/// </summary>
internal sealed class ComboCaptureDialog
{
    private readonly TrayApplicationContext _app;
    private readonly MainWindow _window;

    private ComboCaptureDialog(TrayApplicationContext app, MainWindow window)
    {
        _app = app;
        _window = window;
    }

    public static Task ShowAsync(TrayApplicationContext app, MainWindow window) => new ComboCaptureDialog(app, window).RunAsync();

    private async Task RunAsync()
    {
        var family = _app.Settings.ButtonIcons;
        var glyphs = new ComboGlyphs { Family = family, Buttons = GamepadButtons.None, HorizontalAlignment = HorizontalAlignment.Center };
        var progress = new System.Windows.Controls.ProgressBar { Minimum = 0, Maximum = 1, Height = 4, Margin = new Thickness(0, 16, 0, 0) };
        var status = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
        status.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        var content = new StackPanel { Width = 380 };
        content.Children.Add(new TextBlock { Text = "Mantén en el mando los botones que quieras", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        content.Children.Add(new Border { MinHeight = 40, Child = glyphs });
        content.Children.Add(progress);
        content.Children.Add(status);

        var dialog = new ContentDialog(_window.Dialogs)
        {
            Title = "Combinación para abrir el overlay",
            Content = content,
            PrimaryButtonText = "Guardar",
            CloseButtonText = "Cancelar",
            IsPrimaryButtonEnabled = false,
        };

        var navigationLabels = string.Join(", ", new[] { GamepadButtons.A, GamepadButtons.B }.Select(b => ButtonGlyphs.For(family, b).Text));
        using var session = _app.BeginComboCapture();
        var capture = new ComboCapture();
        var clock = Stopwatch.StartNew();
        GamepadButtons? captured = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        timer.Tick += (_, _) =>
        {
            var state = capture.Update(session.HeldButtons, clock.Elapsed);
            captured = state.Captured;
            glyphs.Buttons = state.Held != GamepadButtons.None ? state.Held : captured ?? GamepadButtons.None;
            progress.Value = state.Held == GamepadButtons.None ? (captured != null ? 1 : 0) : state.Progress;
            dialog.IsPrimaryButtonEnabled = captured != null;
            status.Text = !session.AnyControllerConnected ? "Conecta un mando Xbox o compatible"
                : state.Held == GamepadButtons.None ? (captured != null ? "Combinación lista" : "")
                : state.Validation switch
                {
                    ComboValidation.TooFewButtons => "Usa al menos 2 botones",
                    ComboValidation.TooManyButtons => "Como máximo 3 botones",
                    ComboValidation.OnlyNavigationButtons => $"Añade un botón distinto de la cruceta, {navigationLabels}",
                    _ => state.Captured == state.Held ? "Combinación lista" : "Mantén los botones…",
                };
        };
        // Hiding the window (X, Alt+F4) leaves the dialog pending: close it so the gamepad is not left suspended.
        System.Windows.DependencyPropertyChangedEventHandler onVisibility = (_, _) =>
        {
            if (!_window.IsVisible)
            {
                dialog.Hide(ContentDialogResult.None);
            }
        };
        _window.IsVisibleChanged += onVisibility;
        timer.Start();
        try
        {
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && captured is { } combo)
            {
                _app.SetOverlayCombo(combo);
                _window.OverlaySettingsChanged();
                _window.ShowStatus("Combinación del overlay guardada.");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the overlay combination", ex);
            _window.ShowStatus($"No se pudo guardar la combinación: {ex.Message}", error: true);
        }
        finally
        {
            timer.Stop();
            _window.IsVisibleChanged -= onVisibility;
        }
    }
}
