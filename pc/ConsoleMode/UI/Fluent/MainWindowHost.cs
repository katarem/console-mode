using System.Diagnostics;
using System.Windows.Forms.Integration;
using ConsoleMode.Core;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using WpfApplication = System.Windows.Application;

namespace ConsoleMode.UI.Fluent;

/// <summary>
/// Creates the WPF main window inside the WinForms message loop of the tray. The first call creates a WPF
/// <see cref="System.Windows.Application"/> that never runs (<c>Run</c> is not called): it only holds the WPF-UI
/// resources and theme. Its dispatcher lives on this same UI thread and the WinForms loop pumps its messages.
/// </summary>
internal static class MainWindowHost
{
    private const string ResourcesUri = "pack://application:,,,/ConsoleMode;component/UI/Fluent/AppResources.xaml";

    public static IMainWindow Create(TrayApplicationContext app)
    {
        var clock = Stopwatch.StartNew();
        EnsureApplication();
        var window = new MainWindow(app);
        // Without this, a WPF window opened from a WinForms loop receives no keyboard input.
        ElementHost.EnableModelessKeyboardInterop(window);
        Log.Info($"Main window created in {clock.ElapsedMilliseconds} ms");
        return window;
    }

    private static void EnsureApplication()
    {
        if (WpfApplication.Current != null)
        {
            return;
        }
        var application = new WpfApplication { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        application.DispatcherUnhandledException += (_, e) =>
        {
            Log.Error("Unhandled exception (WPF UI)", e.Exception);
            e.Handled = true;
        };
        // WPF-UI only uses the application's resources if its own dictionaries are at their top level when it first
        // looks at them (UiApplication.Current, cached per thread). Nested inside our dictionary they were ignored: the
        // theme went to a private copy, so the window turned dark while the pages kept the light colours. Add them first,
        // before anything touches WPF-UI's theme manager.
        var resources = application.Resources.MergedDictionaries;
        resources.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
        resources.Add(new ControlsDictionary());
        resources.Add(new System.Windows.ResourceDictionary { Source = new Uri(ResourcesUri) });
        ApplicationThemeManager.ApplySystemTheme();
        Log.Info($"Window theme: {ApplicationThemeManager.GetAppTheme()}");
        AppAccent.Apply();
        // A theme change re-applies WPF-UI's accent: keep the violet one.
        ApplicationThemeManager.Changed += (_, _) => AppAccent.Apply();
    }
}
