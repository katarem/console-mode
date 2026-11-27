using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConsoleMode.Core;
using Wpf.Ui.Appearance;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using VerticalAlignment = System.Windows.VerticalAlignment;
using WpfColor = System.Windows.Media.Color;

namespace ConsoleMode.UI.Fluent;

internal static class Glyphs
{
    /// <summary>Same icon fonts as the gamepad overlay: Segoe Fluent Icons on Windows 11, MDL2 Assets on Windows 10.</summary>
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily SymbolFont = new("Segoe UI Symbol");
    public static readonly FontFamily TextFont = new("Segoe UI");

    public static SolidColorBrush Brush(Rgb color)
    {
        var brush = new SolidColorBrush(WpfColor.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}

/// <summary>Coloured rounded square with a white icon, as in the gamepad overlay.</summary>
public sealed class IconChip : Border
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(IconChip), new PropertyMetadata("", (d, _) => ((IconChip)d).Update()));

    public static readonly DependencyProperty ChipColorProperty = DependencyProperty.Register(
        nameof(ChipColor), typeof(WpfColor), typeof(IconChip), new PropertyMetadata(AppAccent.Color, (d, _) => ((IconChip)d).Update()));

    private readonly TextBlock _icon = new()
    {
        FontFamily = Glyphs.IconFont,
        FontSize = 18,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public IconChip()
    {
        Width = 36;
        Height = 36;
        CornerRadius = new CornerRadius(8);
        VerticalAlignment = VerticalAlignment.Center;
        Child = _icon;
        Update();
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public WpfColor ChipColor
    {
        get => (WpfColor)GetValue(ChipColorProperty);
        set => SetValue(ChipColorProperty, value);
    }

    private void Update()
    {
        _icon.Text = Glyph;
        Background = new SolidColorBrush(ChipColor);
    }
}

/// <summary>
/// One gamepad button drawn with the chosen console's icon (Kenney «Input Prompts»), in the variant for the window
/// theme. If an icon is missing, the button is drawn with its label instead.
/// </summary>
public sealed class ButtonGlyphView : Border
{
    private const double IconSize = 30;

    public static readonly DependencyProperty ButtonProperty = DependencyProperty.Register(
        nameof(Button), typeof(GamepadButtons), typeof(ButtonGlyphView),
        new PropertyMetadata(GamepadButtons.A, (d, _) => ((ButtonGlyphView)d).Update()));

    public static readonly DependencyProperty FamilyProperty = DependencyProperty.Register(
        nameof(Family), typeof(ButtonIconFamily), typeof(ButtonGlyphView),
        new PropertyMetadata(ButtonIconFamily.Xbox, (d, _) => ((ButtonGlyphView)d).Update()));

    /// <summary>True when drawn on a dark surface whatever the theme (the overlay preview); null follows the theme.</summary>
    public static readonly DependencyProperty DarkBackgroundProperty = DependencyProperty.Register(
        nameof(DarkBackground), typeof(bool?), typeof(ButtonGlyphView),
        new PropertyMetadata(null, (d, _) => ((ButtonGlyphView)d).Update()));

    private static readonly Dictionary<string, BitmapImage?> Icons = new();

    private readonly Image _icon = new() { Width = IconSize, Height = IconSize, Stretch = Stretch.Uniform };
    private readonly TextBlock _label = new()
    {
        FontWeight = FontWeights.Bold,
        FontSize = 12,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public ButtonGlyphView()
    {
        VerticalAlignment = VerticalAlignment.Center;
        RenderOptions.SetBitmapScalingMode(_icon, BitmapScalingMode.HighQuality);
        Loaded += (_, _) => ApplicationThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => ApplicationThemeManager.Changed -= OnThemeChanged;
        Update();
    }

    public GamepadButtons Button
    {
        get => (GamepadButtons)GetValue(ButtonProperty);
        set => SetValue(ButtonProperty, value);
    }

    public ButtonIconFamily Family
    {
        get => (ButtonIconFamily)GetValue(FamilyProperty);
        set => SetValue(FamilyProperty, value);
    }

    public bool? DarkBackground
    {
        get => (bool?)GetValue(DarkBackgroundProperty);
        set => SetValue(DarkBackgroundProperty, value);
    }

    private void OnThemeChanged(ApplicationTheme theme, WpfColor accent) => Update();

    private void Update()
    {
        var glyph = ButtonGlyphs.For(Family, Button);
        ToolTip = glyph.Text;
        var dark = DarkBackground ?? ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        if (LoadIcon(ButtonIcons.ResourceName(Family, Button, dark)) is { } image)
        {
            _icon.Source = image;
            Child = _icon;
            Background = Brushes.Transparent;
            Width = IconSize;
            Height = IconSize;
            MinWidth = 0;
            Padding = new Thickness(0);
            CornerRadius = new CornerRadius(0);
            return;
        }

        _label.Text = glyph.Text;
        _label.FontFamily = glyph.UsesSymbolFont ? Glyphs.SymbolFont : Glyphs.TextFont;
        _label.FontSize = glyph.UsesSymbolFont ? 14 : 12;
        _label.Foreground = Glyphs.Brush(glyph.Foreground);
        Child = _label;
        Background = Glyphs.Brush(glyph.Fill);
        Height = 28;
        if (glyph.Shape == GlyphShape.Circle)
        {
            Width = 28;
            MinWidth = 0;
            Padding = new Thickness(0);
            CornerRadius = new CornerRadius(14);
        }
        else
        {
            Width = double.NaN;
            MinWidth = 36;
            Padding = new Thickness(9, 0, 9, 0);
            CornerRadius = new CornerRadius(8);
        }
    }

    /// <summary>The embedded icon, loaded once; null if it is missing.</summary>
    private static BitmapImage? LoadIcon(string resourceName)
    {
        if (Icons.TryGetValue(resourceName, out var cached))
        {
            return cached;
        }
        BitmapImage? image = null;
        using (var stream = typeof(ButtonGlyphView).Assembly.GetManifestResourceStream(resourceName))
        {
            if (stream != null)
            {
                image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
            }
            else
            {
                Log.Error($"Button icon not found: {resourceName}");
            }
        }
        Icons[resourceName] = image;
        return image;
    }
}

/// <summary>A button combination drawn as glyphs joined by «+».</summary>
public sealed class ComboGlyphs : StackPanel
{
    public static readonly DependencyProperty ButtonsProperty = DependencyProperty.Register(
        nameof(Buttons), typeof(GamepadButtons), typeof(ComboGlyphs),
        new PropertyMetadata(OverlayCombos.Default, (d, _) => ((ComboGlyphs)d).Update()));

    public static readonly DependencyProperty FamilyProperty = DependencyProperty.Register(
        nameof(Family), typeof(ButtonIconFamily), typeof(ComboGlyphs),
        new PropertyMetadata(ButtonIconFamily.Xbox, (d, _) => ((ComboGlyphs)d).Update()));

    public ComboGlyphs()
    {
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        Update();
    }

    public GamepadButtons Buttons
    {
        get => (GamepadButtons)GetValue(ButtonsProperty);
        set => SetValue(ButtonsProperty, value);
    }

    public ButtonIconFamily Family
    {
        get => (ButtonIconFamily)GetValue(FamilyProperty);
        set => SetValue(FamilyProperty, value);
    }

    private void Update()
    {
        Children.Clear();
        var first = true;
        foreach (var button in ButtonGlyphs.InDisplayOrder(Buttons))
        {
            if (!first)
            {
                var plus = new TextBlock { Text = "+", Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                plus.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                Children.Add(plus);
            }
            Children.Add(new ButtonGlyphView { Button = button, Family = Family });
            first = false;
        }
    }
}

/// <summary>Imitates the hints at the bottom of the gamepad overlay («A Elegir · B Cerrar») with the chosen icons.</summary>
public sealed class OverlayHintPreview : Border
{
    public static readonly DependencyProperty FamilyProperty = DependencyProperty.Register(
        nameof(Family), typeof(ButtonIconFamily), typeof(OverlayHintPreview),
        new PropertyMetadata(ButtonIconFamily.Xbox, (d, _) => ((OverlayHintPreview)d).Update()));

    // Same colours as the overlay, whatever the window theme.
    private static readonly SolidColorBrush OverlayBackground = Glyphs.Brush(new Rgb(22, 22, 30));
    private static readonly SolidColorBrush OverlayMuted = Glyphs.Brush(new Rgb(150, 150, 172));

    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

    public OverlayHintPreview()
    {
        Background = OverlayBackground;
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(8, 5, 14, 5);
        VerticalAlignment = VerticalAlignment.Center;
        Child = _row;
        Update();
    }

    public ButtonIconFamily Family
    {
        get => (ButtonIconFamily)GetValue(FamilyProperty);
        set => SetValue(FamilyProperty, value);
    }

    private void Update()
    {
        _row.Children.Clear();
        Add(GamepadButtons.A, "Elegir", new Thickness(0, 0, 14, 0));
        Add(GamepadButtons.B, "Cerrar", new Thickness(0));
    }

    private void Add(GamepadButtons button, string text, Thickness margin)
    {
        _row.Children.Add(new ButtonGlyphView { Button = button, Family = Family, DarkBackground = true });
        _row.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = OverlayMuted,
            FontSize = 13,
            Margin = new Thickness(8, 0, margin.Right, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
    }
}
