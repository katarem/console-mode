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
/// Convierte el estado de un mando (botones XInput + stick izquierdo) en acciones del overlay:
/// mantener View + Menú abre/cierra, la cruceta o el stick navegan arriba/abajo y ajustan a izquierda/derecha
/// (con repetición al mantener),
/// A elige y B cierra. No depende de Windows para poder probarse aparte.
/// </summary>
public sealed class GamepadNavigator
{
    public const ushort DpadUp = 0x0001;
    public const ushort DpadDown = 0x0002;
    public const ushort DpadLeft = 0x0004;
    public const ushort DpadRight = 0x0008;
    public const ushort Start = 0x0010; // Menú
    public const ushort Back = 0x0020;  // View
    public const ushort ButtonA = 0x1000;
    public const ushort ButtonB = 0x2000;

    private const ushort Combo = Start | Back;
    private const short StickThreshold = 16000;

    public TimeSpan ComboHold { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RepeatDelay { get; init; } = TimeSpan.FromMilliseconds(400);
    public TimeSpan RepeatInterval { get; init; } = TimeSpan.FromMilliseconds(120);

    private ushort _previous;
    private TimeSpan? _comboSince;
    private bool _comboFired;
    private int _direction;
    private TimeSpan _nextRepeat;

    public List<PadAction> Update(ushort buttons, short leftStickX, short leftStickY, TimeSpan now, bool overlayOpen)
    {
        var actions = new List<PadAction>();

        // ----- Combinación View + Menú mantenida -----
        if ((buttons & Combo) == Combo)
        {
            _comboSince ??= now;
            if (!_comboFired && now - _comboSince >= ComboHold)
            {
                _comboFired = true;
                actions.Add(PadAction.ToggleOverlay);
            }
            _previous = buttons;
            _direction = 0;
            return actions; // mientras se mantiene la combinación no se navega
        }
        _comboSince = null;
        _comboFired = false;

        if (!overlayOpen)
        {
            _previous = buttons;
            _direction = 0;
            return actions;
        }

        // ----- Dirección con repetición (arriba/abajo tiene prioridad sobre izquierda/derecha) -----
        var direction = (buttons & DpadUp) != 0 || leftStickY > StickThreshold ? 1
            : (buttons & DpadDown) != 0 || leftStickY < -StickThreshold ? 2
            : (buttons & DpadLeft) != 0 || leftStickX < -StickThreshold ? 3
            : (buttons & DpadRight) != 0 || leftStickX > StickThreshold ? 4
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

        // ----- A / B al pulsar (no al mantener) -----
        if (Pressed(buttons, ButtonA))
        {
            actions.Add(PadAction.Accept);
        }
        if (Pressed(buttons, ButtonB))
        {
            actions.Add(PadAction.Back);
        }

        _previous = buttons;
        return actions;
    }

    private static PadAction ToAction(int direction) => direction switch
    {
        1 => PadAction.Up,
        2 => PadAction.Down,
        3 => PadAction.Left,
        _ => PadAction.Right,
    };

    private bool Pressed(ushort buttons, ushort mask) => (buttons & mask) != 0 && (_previous & mask) == 0;
}
