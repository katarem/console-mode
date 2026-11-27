namespace ConsoleMode.Core;

public enum ControllerPower
{
    Wired,
    Battery,
}

/// <summary>Battery of a gamepad as XInput reports it: wired, or a battery with 0–3 bars.</summary>
public readonly record struct ControllerBattery(ControllerPower Power, int Bars)
{
    public const int MaxBars = 3;

    // XInput BATTERY_TYPE_* and BATTERY_LEVEL_* values.
    private const byte TypeDisconnected = 0x00;
    private const byte TypeWired = 0x01;
    private const byte TypeAlkaline = 0x02;
    private const byte TypeNimh = 0x03;
    private const byte TypeUnknown = 0xFF;

    /// <summary>
    /// Reads XINPUT_BATTERY_INFORMATION. Null when the controller is disconnected or the battery type is unknown
    /// (some third-party controllers), so nothing is shown instead of a wrong level.
    /// </summary>
    public static ControllerBattery? FromXInput(byte batteryType, byte batteryLevel) => batteryType switch
    {
        TypeDisconnected or TypeUnknown => null,
        TypeWired => new ControllerBattery(ControllerPower.Wired, MaxBars),
        TypeAlkaline or TypeNimh => new ControllerBattery(ControllerPower.Battery, Math.Clamp((int)batteryLevel, 0, MaxBars)),
        _ => null,
    };

    /// <summary>Only a battery can run out: a wired controller is never low.</summary>
    public bool IsLow => Power == ControllerPower.Battery && Bars <= 1;

    public string Label => Power == ControllerPower.Wired
        ? "Con cable"
        : Bars switch
        {
            0 => "Vacía",
            1 => "Baja",
            2 => "Media",
            _ => "Llena",
        };
}
