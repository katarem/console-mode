namespace ConsoleMode.Core;

public enum ComboValidation
{
    Valid,
    TooFewButtons,
    TooManyButtons,
    OnlyNavigationButtons,
}

/// <summary>Rules for the button combination that opens the gamepad overlay, and how it is stored.</summary>
public static class OverlayCombos
{
    public const GamepadButtons Default = GamepadButtons.View | GamepadButtons.Menu;
    public const int MinButtons = 2;
    public const int MaxButtons = 3;

    /// <summary>
    /// Buttons that navigate the overlay. A combination made only of these would fire while moving around in a game or
    /// in the frontend.
    /// </summary>
    public const GamepadButtons NavigationButtons =
        GamepadButtons.DPadUp | GamepadButtons.DPadDown | GamepadButtons.DPadLeft | GamepadButtons.DPadRight |
        GamepadButtons.A | GamepadButtons.B;

    public static ComboValidation Validate(GamepadButtons combo)
    {
        var count = combo.Count();
        if (count < MinButtons)
        {
            return ComboValidation.TooFewButtons;
        }
        if (count > MaxButtons)
        {
            return ComboValidation.TooManyButtons;
        }
        return (combo & ~NavigationButtons) == 0 ? ComboValidation.OnlyNavigationButtons : ComboValidation.Valid;
    }

    /// <summary>Button names as stored in settings.json, lowest bit first.</summary>
    public static List<string> ToNames(GamepadButtons combo) => combo.Split().Select(b => b.ToString()).ToList();

    /// <summary>
    /// Reads the stored names leniently: unknown, numeric or composite names are skipped, and anything that is not a
    /// valid combination gives the default one. A hand-edited value therefore never breaks the rest of the settings.
    /// </summary>
    public static GamepadButtons FromNames(IEnumerable<string>? names)
    {
        var combo = GamepadButtons.None;
        foreach (var name in names ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(name) && !char.IsDigit(name.Trim()[0]) &&
                Enum.TryParse<GamepadButtons>(name.Trim(), ignoreCase: true, out var button) &&
                Enum.IsDefined(button) && button.Count() == 1)
            {
                combo |= button;
            }
        }
        return Validate(combo) == ComboValidation.Valid ? combo : Default;
    }
}
