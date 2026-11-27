using ConsoleMode.Core;
using static ConsoleMode.Core.GamepadButtons;

namespace ConsoleMode.Tests;

public class GamepadInputTests
{
    [Fact]
    public void XInputBitsMapToButtons() =>
        Assert.Equal(A | View | DPadUp, GamepadInput.FromXInput(0x1000 | 0x0020 | 0x0001, 0, 0));

    [Fact]
    public void ReservedBitsAreDropped() => Assert.Equal(None, GamepadInput.FromXInput(0x0400 | 0x0800, 0, 0));

    [Theory]
    [InlineData(30, false)]
    [InlineData(31, true)]
    public void TriggersCountAboveTheThreshold(byte value, bool pressed)
    {
        var buttons = GamepadInput.FromXInput(0, value, value);

        Assert.Equal(pressed ? LeftTrigger | RightTrigger : None, buttons);
    }

    [Fact]
    public void SplitGivesSingleButtonsLowestFirst() =>
        Assert.Equal(new[] { DPadUp, View, A, LeftTrigger }, (LeftTrigger | A | View | DPadUp).Split());

    [Fact]
    public void CountCountsButtons() => Assert.Equal(3, (A | B | RightTrigger).Count());
}
