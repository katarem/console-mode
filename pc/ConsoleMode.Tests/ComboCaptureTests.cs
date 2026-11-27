using ConsoleMode.Core;
using static ConsoleMode.Core.GamepadButtons;

namespace ConsoleMode.Tests;

public class ComboCaptureTests
{
    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);

    [Fact]
    public void CapturesAValidSetHeldForOneSecond()
    {
        var capture = new ComboCapture();

        capture.Update(LeftBumper | RightBumper, Ms(0));
        Assert.Null(capture.Update(LeftBumper | RightBumper, Ms(990)).Captured);

        var state = capture.Update(LeftBumper | RightBumper, Ms(1000));
        Assert.Equal(LeftBumper | RightBumper, state.Captured);
        Assert.Equal(1, state.Progress);
    }

    [Fact]
    public void ReportsProgressWhileHolding()
    {
        var capture = new ComboCapture();

        capture.Update(View | Menu, Ms(0));

        Assert.Equal(0.5, capture.Update(View | Menu, Ms(500)).Progress, precision: 3);
    }

    [Theory]
    [InlineData(Y, ComboValidation.TooFewButtons)]
    [InlineData(LeftBumper | RightBumper | X | Y, ComboValidation.TooManyButtons)]
    [InlineData(DPadDown | A, ComboValidation.OnlyNavigationButtons)]
    public void InvalidSetsAreNeverCaptured(GamepadButtons held, ComboValidation validation)
    {
        var capture = new ComboCapture();

        capture.Update(held, Ms(0));
        var state = capture.Update(held, Ms(5000));

        Assert.Equal(validation, state.Validation);
        Assert.Equal(0, state.Progress);
        Assert.Null(state.Captured);
    }

    [Fact]
    public void ReleasingKeepsTheCapture()
    {
        var capture = new ComboCapture();
        capture.Update(X | Y, Ms(0));
        capture.Update(X | Y, Ms(1000));

        var state = capture.Update(None, Ms(1100));

        Assert.Equal(None, state.Held);
        Assert.Equal(X | Y, state.Captured);
    }

    [Fact]
    public void ChangingTheSetRestartsTheTime()
    {
        var capture = new ComboCapture();
        capture.Update(LeftBumper | RightBumper, Ms(0));

        capture.Update(LeftBumper | RightBumper | Y, Ms(800));

        Assert.Null(capture.Update(LeftBumper | RightBumper | Y, Ms(1500)).Captured);
        Assert.Equal(LeftBumper | RightBumper | Y, capture.Update(LeftBumper | RightBumper | Y, Ms(1800)).Captured);
    }

    [Fact]
    public void ALaterValidSetReplacesTheCapture()
    {
        var capture = new ComboCapture();
        capture.Update(X | Y, Ms(0));
        capture.Update(X | Y, Ms(1000));
        capture.Update(None, Ms(1100));

        capture.Update(View | Menu, Ms(1200));

        Assert.Equal(View | Menu, capture.Update(View | Menu, Ms(2200)).Captured);
    }
}
