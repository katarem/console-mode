using ConsoleMode.Core;

namespace ConsoleMode.Tests;

public class GamepadNavigatorTests
{
    private const ushort View = 0x0020;
    private const ushort Menu = 0x0010;
    private const ushort DpadDown = 0x0002;
    private const ushort ButtonA = 0x1000;
    private const ushort ButtonB = 0x2000;

    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);

    [Fact]
    public void HoldingTheDefaultComboForHalfASecondTogglesTheOverlay()
    {
        var navigator = new GamepadNavigator();

        Assert.Empty(navigator.Update(View | Menu, 0, 0, Ms(0), overlayOpen: false));
        Assert.Equal(new[] { PadAction.ToggleOverlay }, navigator.Update(View | Menu, 0, 0, Ms(500), overlayOpen: false));
    }

    [Fact]
    public void ReleasingTheComboBeforeHalfASecondDoesNothing()
    {
        var navigator = new GamepadNavigator();

        navigator.Update(View | Menu, 0, 0, Ms(0), overlayOpen: false);
        Assert.Empty(navigator.Update(View | Menu, 0, 0, Ms(490), overlayOpen: false));
        Assert.Empty(navigator.Update(0, 0, 0, Ms(600), overlayOpen: false));
    }

    [Fact]
    public void KeepingTheComboHeldTogglesOnlyOnce()
    {
        var navigator = new GamepadNavigator();

        navigator.Update(View | Menu, 0, 0, Ms(0), overlayOpen: false);
        navigator.Update(View | Menu, 0, 0, Ms(500), overlayOpen: false);

        Assert.Empty(navigator.Update(View | Menu, 0, 0, Ms(3000), overlayOpen: true));
    }

    [Fact]
    public void ButtonsDoNotNavigateWhileTheOverlayIsClosed()
    {
        var navigator = new GamepadNavigator();

        Assert.Empty(navigator.Update(ButtonA, 0, 0, Ms(0), overlayOpen: false));
        Assert.Empty(navigator.Update(DpadDown, 0, 0, Ms(100), overlayOpen: false));
    }

    [Fact]
    public void FaceButtonsActOnPressOnly()
    {
        var navigator = new GamepadNavigator();

        Assert.Equal(new[] { PadAction.Accept }, navigator.Update(ButtonA, 0, 0, Ms(0), overlayOpen: true));
        Assert.Empty(navigator.Update(ButtonA, 0, 0, Ms(100), overlayOpen: true));
        Assert.Equal(new[] { PadAction.Back }, navigator.Update(ButtonB, 0, 0, Ms(200), overlayOpen: true));
    }

    [Fact]
    public void HeldDirectionRepeatsAfterTheDelay()
    {
        var navigator = new GamepadNavigator();

        Assert.Equal(new[] { PadAction.Down }, navigator.Update(DpadDown, 0, 0, Ms(0), overlayOpen: true));
        Assert.Empty(navigator.Update(DpadDown, 0, 0, Ms(399), overlayOpen: true));
        Assert.Equal(new[] { PadAction.Down }, navigator.Update(DpadDown, 0, 0, Ms(400), overlayOpen: true));
        Assert.Equal(new[] { PadAction.Down }, navigator.Update(DpadDown, 0, 0, Ms(520), overlayOpen: true));
    }

    [Fact]
    public void StickMovesLikeTheDpad()
    {
        var navigator = new GamepadNavigator();

        Assert.Equal(new[] { PadAction.Up }, navigator.Update(0, 0, 20000, Ms(0), overlayOpen: true));
        Assert.Equal(new[] { PadAction.Left }, navigator.Update(0, -20000, 0, Ms(50), overlayOpen: true));
    }
}
