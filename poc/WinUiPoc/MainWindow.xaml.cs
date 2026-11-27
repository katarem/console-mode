using Microsoft.UI.Xaml;

namespace WinUiPoc;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Nav.Loaded += (_, _) =>
        {
            PocLog.Write($"WinUI NavigationView loaded (XamlRoot scale {Nav.XamlRoot?.RasterizationScale})");
            // WinUI thread -> WinForms thread.
            Program.WinFormsForm?.BeginInvoke(() => PocLog.Write("WinUI -> WinForms call ran on the WinForms thread"));
        };
    }

    public void ShowFromWinForms(string text)
    {
        CrossThread.Text = text;
        PocLog.Write("WinForms -> WinUI call ran on the WinUI thread");
    }
}
