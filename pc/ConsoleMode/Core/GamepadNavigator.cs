namespace ConsoleMode.Core;

public enum PadAction
{
    ToggleOverlay,
    Up,
    Down,
    Left,
    Right,
    Accept,
    Back,
}

/// <summary>
/// Turns the state of a gamepad (buttons + left stick) into overlay actions: holding the overlay combination
/// (<see cref="Combo"/>, View + Menu by default) opens or closes it, the D-pad or the stick move up/down and adjust
/// left/right (repeating while held), A accepts and B goes back. It does not depend on Windows so it can be tested.
/// </summary>
public sealed class GamepadNavigator
{
    private const short StickThreshold = 16000;

    public TimeSpan ComboHold { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan RepeatDelay { get; init; } = TimeSpan.FromMilliseconds(400);
    public TimeSpan RepeatInterval { get; init; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Buttons that, held together for <see cref="ComboHold"/>, toggle the overlay.</summary>
    public GamepadButtons Combo { get; set; } = OverlayCombos.Default;

    /// <summary>
    /// While true (a new combination is being captured) no action is emitted. A combination still held when it goes
    /// back to false does not fire until it is released and held again.
    /// </summary>
    public bool TriggerSuspended { get; set; }

    private GamepadButtons _previous;
    private TimeSpan? _comboSince;
    private bool _comboFired;
    private int _direction;
    private TimeSpan _nextRepeat;

    public List<PadAction> Update(GamepadButtons buttons, short leftStickX, short leftStickY, TimeSpan now, bool overlayOpen)
    {
        var actions = new List<PadAction>();
        var comboHeld = Combo != GamepadButtons.None && (buttons & Combo) == Combo;

        if (TriggerSuspended)
        {
            _comboSince = null;
            _comboFired = comboHeld;
            _previous = buttons;
            _direction = 0;
            return actions;
        }

        // ----- Overlay combination held -----
        if (comboHeld)
        {
            _comboSince ??= now;
            if (!_comboFired && now - _comboSince >= ComboHold)
            {
                _comboFired = true;
                actions.Add(PadAction.ToggleOverlay);
            }
            _previous = buttons;
            _direction = 0;
            return actions; // no navigation while the combination is held
        }
        _comboSince = null;
        _comboFired = false;

        if (!overlayOpen)
        {
            _previous = buttons;
            _direction = 0;
            return actions;
        }

        // ----- Direction with repeat (up/down wins over left/right) -----
        var direction = Has(buttons, GamepadButtons.DPadUp) || leftStickY > StickThreshold ? 1
            : Has(buttons, GamepadButtons.DPadDown) || leftStickY < -StickThreshold ? 2
            : Has(buttons, GamepadButtons.DPadLeft) || leftStickX < -StickThreshold ? 3
            : Has(buttons, GamepadButtons.DPadRight) || leftStickX > StickThreshold ? 4
            : 0;
        if (direction != _direction)
        {
            _direction = direction;
            if (direction != 0)
            {
                actions.Add(ToAction(direction));
                _nextRepeat = now + RepeatDelay;
            }
        }
        else if (direction != 0 && now >= _nextRepeat)
        {
            actions.Add(ToAction(direction));
            _nextRepeat = now + RepeatInterval;
        }

        // ----- A / B on press (not while held) -----
        if (Pressed(buttons, GamepadButtons.A))
        {
            actions.Add(PadAction.Accept);
        }
        if (Pressed(buttons, GamepadButtons.B))
        {
            actions.Add(PadAction.Back);
        }

        _previous = buttons;
        return actions;
    }

    private static bool Has(GamepadButtons buttons, GamepadButtons button) => (buttons & button) != 0;

    private static PadAction ToAction(int direction) => direction switch
    {
        1 => PadAction.Up,
        2 => PadAction.Down,
        3 => PadAction.Left,
        _ => PadAction.Right,
    };

    private bool Pressed(GamepadButtons buttons, GamepadButtons button) => Has(buttons, button) && !Has(_previous, button);
}
