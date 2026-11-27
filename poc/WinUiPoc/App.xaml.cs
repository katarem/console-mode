using Microsoft.UI.Xaml;

namespace WinUiPoc;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => PocLog.Write($"WinUI unhandled exception: {e.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Window = new MainWindow();
        Window.Activate();
        PocLog.Write("WinUI window activated");
    }
}
