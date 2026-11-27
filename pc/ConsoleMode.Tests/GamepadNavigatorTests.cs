using ConsoleMode.Core;
using static ConsoleMode.Core.GamepadButtons;

namespace ConsoleMode.Tests;

public class GamepadNavigatorTests
{
    private static TimeSpan Ms(int value) => TimeSpan.FromMilliseconds(value);

    private static List<PadAction> Hold(GamepadNavigator navigator, GamepadButtons buttons, int ms, bool overlayOpen = false) =>
        navigator.Update(buttons, 0, 0, Ms(ms), overlayOpen);

    [Fact]
    public void HoldingTheDefaultComboForHalfASecondTogglesTheOverlay()
    {
        var navigator = new GamepadNavigator();

        Assert.Empty(Hold(navigator, View | Menu, 0));
        Assert.Equal(new[] { PadAction.ToggleOverlay }, Hold(navigator, View | Menu, 500));
    }

    [Fact]
    public void ReleasingTheComboBeforeHalfASecondDoesNothing()
    {
        var navigator = new GamepadNavigator();

        Hold(navigator, View | Menu, 0);
        Assert.Empty(Hold(navigator, View | Menu, 490));
        Assert.Empty(Hold(navigator, None, 600));
    }

    [Fact]
    public void KeepingTheComboHeldTogglesOnlyOnce()
    {
        var navigator = new GamepadNavigator();

        Hold(navigator, View | Menu, 0);
        Hold(navigator, View | Menu, 500);

        Assert.Empty(Hold(navigator, View | Menu, 3000, overlayOpen: true));
    }

    [Fact]
    public void ACustomComboOpensTheOverlayAndTheDefaultOneNoLonger()
    {
        var navigator = new GamepadNavigator { Combo = LeftBumper | RightBumper | Y };

        Hold(navigator, View | Menu, 0);
        Assert.Empty(Hold(navigator, View | Menu, 1000));
        Hold(navigator, None, 1100);

        Hold(navigator, LeftBumper | RightBumper | Y, 1200);
        Assert.Equal(new[] { PadAction.ToggleOverlay }, Hold(navigator, LeftBumper | RightBumper | Y, 1700));
    }

    [Fact]
    public void AComboCanUseTheTriggers()
    {
        var navigator = new GamepadNavigator { Combo = LeftTrigger | RightTrigger };

        Hold(navigator, LeftTrigger | RightTrigger, 0);
        Assert.Equal(new[] { PadAction.ToggleOverlay }, Hold(navigator, LeftTrigger | RightTrigger, 500));
    }

    [Fact]
    public void HoldingAnExtraButtonStillOpensTheOverlay()
    {
        var navigator = new GamepadNavigator();

        Hold(navigator, View | Menu | X, 0);
        Assert.Equal(new[] { PadAction.ToggleOverlay }, Hold(navigator, View | Menu | X, 500));
    }

    [Fact]
    public void ChangingTheComboAppliesOnTheNextReading()
    {
        var navigator = new GamepadNavigator();
        navigator.Combo = A | X;

        Hold(navigator, A | X, 0);
        Assert.Equal(new[] { PadAction.ToggleOverlay }, Hold(navigator, A | X, 500));
    }

    [Fact]
    public void WhileSuspendedNothingFiresEvenWithTheComboHeld()
    {
        var navigator = new GamepadNavigator { TriggerSuspended = true };

        Assert.Empty(Hold(navigator, View | Menu, 0));
        Assert.Empty(Hold(navigator, View | Menu, 2000));
        Assert.Empty(Hold(navigator, A, 2100, overlayOpen: true));
    }

    [Fact]
    public void AfterResumingAComboStillHeldMustBeReleasedFirst()
    {
        var navigator = new GamepadNavigator { TriggerSuspended = true };
        Hold(navigator, View | Menu, 0);

        navigator.TriggerSuspended = false;
        Assert.Empty(Hold(navigator, View | Menu, 100));
        Assert.Empty(Hold(navigator, View | Menu, 2000));

        Hold(navigator, None, 2100);
        Hold(navigator, View | Menu, 2200);
        Assert.Equal(new[] { PadAction.ToggleOverlay }, Hold(navigator, View | Menu, 2700));
    }

    [Fact]
    public void SavingANewComboWhileItIsHeldDoesNotOpenTheOverlay()
    {
        // The capture dialog: the gamepad is suspended while the user holds LB + RB, then the combination is saved
        // and the suspension ends before the next reading.
        var navigator = new GamepadNavigator { TriggerSuspended = true };
        Hold(navigator, LeftBumper | RightBumper, 0);
        Hold(navigator, LeftBumper | RightBumper, 1000);

        navigator.Combo = LeftBumper | RightBumper;
        navigator.TriggerSuspended = false;
        Assert.Empty(Hold(navigator, LeftBumper | RightBumper, 1100));
        Assert.Empty(Hold(navigator, LeftBumper | RightBumper, 1600));
        Assert.Empty(Hold(navigator, LeftBumper | RightBumper, 3000));

        Hold(navigator, None, 3100);
        Hold(navigator, LeftBumper | RightBumper, 3200);
        Assert.Equal(new[] { PadAction.ToggleOverlay }, Hold(navigator, LeftBumper | RightBumper, 3700));
    }

    [Fact]
    public void ButtonsDoNotNavigateWhileTheOverlayIsClosed()
    {
        var navigator = new GamepadNavigator();

        Assert.Empty(Hold(navigator, A, 0));
        Assert.Empty(Hold(navigator, DPadDown, 100));
    }

    [Fact]
    public void FaceButtonsActOnPressOnly()
    {
        var navigator = new GamepadNavigator();

        Assert.Equal(new[] { PadAction.Accept }, Hold(navigator, A, 0, overlayOpen: true));
        Assert.Empty(Hold(navigator, A, 100, overlayOpen: true));
        Assert.Equal(new[] { PadAction.Back }, Hold(navigator, B, 200, overlayOpen: true));
    }

    [Fact]
    public void HeldDirectionRepeatsAfterTheDelay()
    {
        var navigator = new GamepadNavigator();

        Assert.Equal(new[] { PadAction.Down }, Hold(navigator, DPadDown, 0, overlayOpen: true));
        Assert.Empty(Hold(navigator, DPadDown, 399, overlayOpen: true));
        Assert.Equal(new[] { PadAction.Down }, Hold(navigator, DPadDown, 400, overlayOpen: true));
        Assert.Equal(new[] { PadAction.Down }, Hold(navigator, DPadDown, 520, overlayOpen: true));
    }

    [Fact]
    public void StickMovesLikeTheDpad()
    {
        var navigator = new GamepadNavigator();

        Assert.Equal(new[] { PadAction.Up }, navigator.Update(None, 0, 20000, Ms(0), overlayOpen: true));
        Assert.Equal(new[] { PadAction.Left }, navigator.Update(None, -20000, 0, Ms(50), overlayOpen: true));
    }
}
