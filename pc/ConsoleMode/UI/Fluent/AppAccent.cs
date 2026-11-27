using Wpf.Ui.Appearance;
using WpfColor = System.Windows.Media.Color;

namespace ConsoleMode.UI.Fluent;

/// <summary>Console Mode's violet accent, applied over the system accent in both themes.</summary>
internal static class AppAccent
{
    public static readonly WpfColor Color = WpfColor.FromRgb(76, 70, 229);

    public static void Apply() => ApplicationAccentColorManager.Apply(Color, ApplicationThemeManager.GetAppTheme());
}
