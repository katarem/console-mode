using System.Reflection;
using ConsoleMode.Core;

namespace ConsoleMode.UI;

/// <summary>Button icons for the WinForms overlay (always on a dark background), loaded once from the app resources.</summary>
internal static class ButtonIconImages
{
    private static readonly Dictionary<string, Image?> Cache = new();

    /// <summary>The icon for a dark background, or null if it is not embedded (the caller draws the label instead).</summary>
    public static Image? ForDarkBackground(ButtonIconFamily family, GamepadButtons button)
    {
        var name = ButtonIcons.ResourceName(family, button, darkBackground: true);
        lock (Cache)
        {
            if (!Cache.TryGetValue(name, out var image))
            {
                image = Load(name);
                Cache[name] = image;
            }
            return image;
        }
    }

    private static Image? Load(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (stream == null)
        {
            Log.Error($"Button icon not found: {name}");
            return null;
        }
        // Copied: GDI+ needs the stream to stay open for as long as the image lives.
        using var copy = new Bitmap(stream);
        return new Bitmap(copy);
    }
}
