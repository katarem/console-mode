using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ConsoleMode.Core;
using ConsoleMode.Network;
using QRCoder;
using Wpf.Ui.Controls;
using Button = Wpf.Ui.Controls.Button;
using TextBlock = System.Windows.Controls.TextBlock;

namespace ConsoleMode.UI.Fluent;

/// <summary>Pairing QR code and the list of trusted phones.</summary>
public partial class DevicesPage : Page
{
    private readonly TrayApplicationContext _app;
    private readonly MainWindow _window;

    private AppSettings Settings => _app.Settings;

    public DevicesPage(TrayApplicationContext app, MainWindow window)
    {
        _app = app;
        _window = window;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshQr();
            RefreshDevices();
        };
    }

    public void RefreshDevices()
    {
        List<TrustedDevice> devices;
        lock (Settings)
        {
            devices = Settings.TrustedDevices.ToList();
        }

        DeviceRows.Children.Clear();
        foreach (var device in devices.OrderBy(d => d.Name))
        {
            DeviceRows.Children.Add(DeviceRow(device));
        }
        NoDevices.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border DeviceRow(TrustedDevice device)
    {
        var remove = new Button
        {
            Icon = new SymbolIcon { Symbol = SymbolRegular.Delete24 },
            Appearance = ControlAppearance.Transparent,
            ToolTip = "Quitar dispositivo",
            VerticalAlignment = VerticalAlignment.Center,
        };
        remove.Click += async (_, _) => await RemoveAsync(device);

        var title = new TextBlock { Text = device.Name };
        title.SetResourceReference(StyleProperty, "RowTitle");
        var details = new TextBlock { Text = $"Añadido {device.AddedAt:g} · Última conexión {device.LastSeenAt?.ToString("g") ?? "—"}" };
        details.SetResourceReference(StyleProperty, "Secondary");
        var text = new StackPanel { Margin = new Thickness(16, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(details);

        var chip = new IconChip { Glyph = "\uE8EA", ChipColor = System.Windows.Media.Color.FromRgb(59, 130, 246) };
        var row = new DockPanel();
        DockPanel.SetDock(chip, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(chip);
        row.Children.Add(remove);
        row.Children.Add(text);

        var card = new Border { Child = row };
        card.SetResourceReference(StyleProperty, "CardRow");
        return card;
    }

    private async Task RemoveAsync(TrustedDevice device)
    {
        var dialog = new ContentDialog(_window.Dialogs)
        {
            Title = $"¿Quitar «{device.Name}» de los dispositivos de confianza?",
            Content = "Tendrá que volver a emparejarse.",
            PrimaryButtonText = "Quitar",
            CloseButtonText = "Cancelar",
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _app.RemoveTrustedDevice(device.Id);
            RefreshDevices();
            _window.DeviceCountChanged();
        }
    }

    private void RefreshQr()
    {
        try
        {
            var uri = NetworkInfo.PairingUri(Settings);
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.M);
            var png = new PngByteQRCode(data).GetGraphic(8);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(png);
            image.EndInit();
            image.Freeze();
            Qr.Source = image;

            var ips = NetworkInfo.GetLocalAddresses();
            Address.Text = ips.Count == 0
                ? "No se ha detectado ninguna red."
                : $"{Environment.MachineName}\n{string.Join("\n", ips.Select(ip => $"{ip}:{Settings.Port}"))}";
        }
        catch (Exception ex)
        {
            Address.Text = $"No se pudo generar el QR: {ex.Message}";
        }
    }
}
