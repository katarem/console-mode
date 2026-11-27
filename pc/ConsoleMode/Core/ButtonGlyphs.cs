namespace ConsoleMode.Core;

/// <summary>Console whose button icons the overlay and the window show. Input is always read through XInput.</summary>
public enum ButtonIconFamily
{
    Xbox,
    PlayStation,
    Nintendo,
}

public enum GlyphShape
{
    Circle,
    Pill,
}

public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>How one gamepad button is drawn: its label, shape and colours.</summary>
/// <param name="UsesSymbolFont">The label is a symbol (PlayStation ✕ ○ □ △) that needs a symbol font.</param>
public sealed record ButtonGlyph(string Text, GlyphShape Shape, Rgb Fill, Rgb Foreground, bool UsesSymbolFont);

/// <summary>
/// Button icons of each console family. Buttons are matched by their physical position, so the bottom face button
/// (XInput A) is «A» on Xbox, «✕» on PlayStation and «B» on Nintendo.
/// </summary>
public static class ButtonGlyphs
{
    private static readonly Rgb White = new(255, 255, 255);
    private static readonly Rgb Neutral = new(60, 60, 72);

    public static ButtonGlyph For(ButtonIconFamily family, GamepadButtons button)
    {
        if (button.Count() != 1 || !Enum.IsDefined(button))
        {
            throw new ArgumentException($"Expected a single button, got {button}", nameof(button));
        }

        return button switch
        {
            GamepadButtons.A => Face(family, "A", new Rgb(16, 124, 16), "✕", new Rgb(124, 178, 232), "B"),
            GamepadButtons.B => Face(family, "B", new Rgb(200, 40, 40), "○", new Rgb(255, 102, 102), "A"),
            GamepadButtons.X => Face(family, "X", new Rgb(17, 88, 199), "□", new Rgb(255, 105, 248), "Y"),
            GamepadButtons.Y => Face(family, "Y", new Rgb(230, 170, 0), "△", new Rgb(64, 226, 160), "X"),
            GamepadButtons.DPadUp => Circle("↑"),
            GamepadButtons.DPadDown => Circle("↓"),
            GamepadButtons.DPadLeft => Circle("←"),
            GamepadButtons.DPadRight => Circle("→"),
            GamepadButtons.View => Pill(family, "View", "Create", "−"),
            GamepadButtons.Menu => Pill(family, "Menú", "Options", "+"),
            GamepadButtons.LeftBumper => Pill(family, "LB", "L1", "L"),
            GamepadButtons.RightBumper => Pill(family, "RB", "R1", "R"),
            GamepadButtons.LeftTrigger => Pill(family, "LT", "L2", "ZL"),
            GamepadButtons.RightTrigger => Pill(family, "RT", "R2", "ZR"),
            GamepadButtons.LeftStick => Pill(family, "LS", "L3", "LS"),
            _ => Pill(family, "RS", "R3", "RS"),
        };
    }

    /// <summary>Order in which a combination is shown, so View + Menu does not read «Menú + View».</summary>
    private static readonly GamepadButtons[] DisplayOrder =
    {
        GamepadButtons.View, GamepadButtons.Menu,
        GamepadButtons.LeftBumper, GamepadButtons.RightBumper, GamepadButtons.LeftTrigger, GamepadButtons.RightTrigger,
        GamepadButtons.LeftStick, GamepadButtons.RightStick,
        GamepadButtons.DPadUp, GamepadButtons.DPadDown, GamepadButtons.DPadLeft, GamepadButtons.DPadRight,
        GamepadButtons.A, GamepadButtons.B, GamepadButtons.X, GamepadButtons.Y,
    };

    /// <summary>One glyph per button of <paramref name="combo"/>, in display order.</summary>
    public static IReadOnlyList<ButtonGlyph> ForCombo(ButtonIconFamily family, GamepadButtons combo) =>
        DisplayOrder.Where(button => (combo & button) != 0).Select(button => For(family, button)).ToList();

    private static ButtonGlyph Face(ButtonIconFamily family, string xbox, Rgb xboxFill, string playStation, Rgb playStationColor,
        string nintendo) => family switch
    {
        ButtonIconFamily.PlayStation => new ButtonGlyph(playStation, GlyphShape.Circle, Neutral, playStationColor, UsesSymbolFont: true),
        ButtonIconFamily.Nintendo => new ButtonGlyph(nintendo, GlyphShape.Circle, Neutral, White, UsesSymbolFont: false),
        _ => new ButtonGlyph(xbox, GlyphShape.Circle, xboxFill, White, UsesSymbolFont: false),
    };

    private static ButtonGlyph Circle(string text) => new(text, GlyphShape.Circle, Neutral, White, UsesSymbolFont: false);

    private static ButtonGlyph Pill(ButtonIconFamily family, string xbox, string playStation, string nintendo) =>
        new(family switch
        {
            ButtonIconFamily.PlayStation => playStation,
            ButtonIconFamily.Nintendo => nintendo,
            _ => xbox,
        }, GlyphShape.Pill, Neutral, White, UsesSymbolFont: false);
}
