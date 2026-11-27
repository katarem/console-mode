namespace ConsoleMode.Core;

/// <summary>What the capture dialog shows after each gamepad reading.</summary>
/// <param name="Held">Buttons held right now.</param>
/// <param name="Validation">Whether the held buttons would make a valid combination.</param>
/// <param name="Progress">Share of <see cref="ComboCapture.HoldTime"/> the held buttons have been kept (0–1).</param>
/// <param name="Captured">Last combination held long enough, kept after the buttons are released.</param>
public sealed record ComboCaptureState(GamepadButtons Held, ComboValidation Validation, double Progress, GamepadButtons? Captured);

/// <summary>
/// Captures a new overlay combination: the user holds the buttons they want and, once a valid set has been held
/// unchanged for <see cref="HoldTime"/>, it becomes the captured combination. Releasing the buttons keeps it; holding
/// another valid set long enough replaces it.
/// </summary>
public sealed class ComboCapture
{
    public static readonly TimeSpan HoldTime = TimeSpan.FromSeconds(1);

    private GamepadButtons _candidate;
    private TimeSpan _since;
    private GamepadButtons? _captured;

    public ComboCaptureState Update(GamepadButtons held, TimeSpan now)
    {
        if (held != _candidate)
        {
            _candidate = held;
            _since = now;
        }
        if (held == GamepadButtons.None)
        {
            return new ComboCaptureState(held, ComboValidation.TooFewButtons, 0, _captured);
        }

        var validation = OverlayCombos.Validate(held);
        var progress = 0.0;
        if (validation == ComboValidation.Valid)
        {
            progress = Math.Clamp((now - _since) / HoldTime, 0, 1);
            if (progress >= 1)
            {
                _captured = held;
            }
        }
        return new ComboCaptureState(held, validation, progress, _captured);
    }
}
