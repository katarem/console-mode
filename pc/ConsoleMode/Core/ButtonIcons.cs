namespace ConsoleMode.Core;

/// <summary>
/// Names of the embedded button icon images (Kenney «Input Prompts», CC0), one per console family, button and background:
/// the «dark» variant is for dark backgrounds (the overlay, the dark theme) and the «light» one for light backgrounds.
/// </summary>
public static class ButtonIcons
{
    public const string ResourcePrefix = "ConsoleMode.ButtonIcons.";

    /// <summary>File name of the icon, as stored in Assets/ButtonIcons.</summary>
    public static string FileName(ButtonIconFamily family, GamepadButtons button, bool darkBackground)
    {
        if (button.Count() != 1 || !Enum.IsDefined(button))
        {
            throw new ArgumentException($"Expected a single button, got {button}", nameof(button));
        }
        return $"{family}.{button}.{(darkBackground ? "dark" : "light")}.png";
    }

    /// <summary>Manifest resource name of the icon embedded in the app.</summary>
    public static string ResourceName(ButtonIconFamily family, GamepadButtons button, bool darkBackground) =>
        ResourcePrefix + FileName(family, button, darkBackground);
}
