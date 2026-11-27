using System.Numerics;

namespace ConsoleMode.Core;

/// <summary>
/// Gamepad buttons with their XInput bit values, plus the two triggers as synthetic bits so that a combination can
/// include them.
/// </summary>
[Flags]
public enum GamepadButtons : uint
{
    None = 0,
    DPadUp = 0x0001,
    DPadDown = 0x0002,
    DPadLeft = 0x0004,
    DPadRight = 0x0008,
    Menu = 0x0010, // XINPUT_GAMEPAD_START
    View = 0x0020, // XINPUT_GAMEPAD_BACK
    LeftStick = 0x0040,
    RightStick = 0x0080,
    LeftBumper = 0x0100,
    RightBumper = 0x0200,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000,
    LeftTrigger = 0x1_0000,
    RightTrigger = 0x2_0000,
}

public static class GamepadInput
{
    /// <summary>A trigger counts as pressed above this value (XINPUT_GAMEPAD_TRIGGER_THRESHOLD).</summary>
    public const byte TriggerThreshold = 30;

    private const ushort KnownButtonBits = 0xF3FF; // 0x0400 and 0x0800 are not buttons in XInput

    public static GamepadButtons FromXInput(ushort buttons, byte leftTrigger, byte rightTrigger)
    {
        var result = (GamepadButtons)(buttons & KnownButtonBits);
        if (leftTrigger > TriggerThreshold)
        {
            result |= GamepadButtons.LeftTrigger;
        }
        if (rightTrigger > TriggerThreshold)
        {
            result |= GamepadButtons.RightTrigger;
        }
        return result;
    }

    public static int Count(this GamepadButtons buttons) => BitOperations.PopCount((uint)buttons);

    /// <summary>The single-button values set in <paramref name="buttons"/>, lowest bit first.</summary>
    public static IEnumerable<GamepadButtons> Split(this GamepadButtons buttons)
    {
        var bits = (uint)buttons;
        while (bits != 0)
        {
            var lowest = bits & (~bits + 1);
            yield return (GamepadButtons)lowest;
            bits &= bits - 1;
        }
    }
}
