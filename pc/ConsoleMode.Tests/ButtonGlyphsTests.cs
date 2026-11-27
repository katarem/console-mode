using ConsoleMode.Core;
using static ConsoleMode.Core.GamepadButtons;

namespace ConsoleMode.Tests;

public class ButtonGlyphsTests
{
    public static TheoryData<GamepadButtons, string, string, string> Table => new()
    {
        { A, "A", "✕", "B" },
        { B, "B", "○", "A" },
        { X, "X", "□", "Y" },
        { Y, "Y", "△", "X" },
        { View, "View", "Create", "−" },
        { Menu, "Menú", "Options", "+" },
        { LeftBumper, "LB", "L1", "L" },
        { RightBumper, "RB", "R1", "R" },
        { LeftTrigger, "LT", "L2", "ZL" },
        { RightTrigger, "RT", "R2", "ZR" },
        { LeftStick, "LS", "L3", "LS" },
        { RightStick, "RS", "R3", "RS" },
        { DPadUp, "↑", "↑", "↑" },
        { DPadDown, "↓", "↓", "↓" },
        { DPadLeft, "←", "←", "←" },
        { DPadRight, "→", "→", "→" },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public void EachFamilyLabelsButtonsByPosition(GamepadButtons button, string xbox, string playStation, string nintendo)
    {
        Assert.Equal(xbox, ButtonGlyphs.For(ButtonIconFamily.Xbox, button).Text);
        Assert.Equal(playStation, ButtonGlyphs.For(ButtonIconFamily.PlayStation, button).Text);
        Assert.Equal(nintendo, ButtonGlyphs.For(ButtonIconFamily.Nintendo, button).Text);
    }

    [Theory]
    [InlineData(A, true)]
    [InlineData(B, true)]
    [InlineData(X, true)]
    [InlineData(Y, true)]
    [InlineData(View, false)]
    [InlineData(DPadUp, false)]
    public void OnlyPlayStationFaceButtonsUseTheSymbolFont(GamepadButtons button, bool symbol)
    {
        Assert.Equal(symbol, ButtonGlyphs.For(ButtonIconFamily.PlayStation, button).UsesSymbolFont);
        Assert.False(ButtonGlyphs.For(ButtonIconFamily.Xbox, button).UsesSymbolFont);
        Assert.False(ButtonGlyphs.For(ButtonIconFamily.Nintendo, button).UsesSymbolFont);
    }

    [Theory]
    [InlineData(A, GlyphShape.Circle)]
    [InlineData(DPadLeft, GlyphShape.Circle)]
    [InlineData(View, GlyphShape.Pill)]
    [InlineData(LeftTrigger, GlyphShape.Pill)]
    public void FaceButtonsAndDpadAreCirclesTheRestPills(GamepadButtons button, GlyphShape shape) =>
        Assert.Equal(shape, ButtonGlyphs.For(ButtonIconFamily.Xbox, button).Shape);

    [Fact]
    public void XboxFaceButtonsKeepTheirColours()
    {
        Assert.Equal(new Rgb(16, 124, 16), ButtonGlyphs.For(ButtonIconFamily.Xbox, A).Fill);
        Assert.Equal(new Rgb(200, 40, 40), ButtonGlyphs.For(ButtonIconFamily.Xbox, B).Fill);
    }

    [Theory]
    [InlineData(None)]
    [InlineData(A | B)]
    public void ForNeedsASingleButton(GamepadButtons button) =>
        Assert.Throws<ArgumentException>(() => ButtonGlyphs.For(ButtonIconFamily.Xbox, button));

    [Fact]
    public void ForComboShowsViewBeforeMenu() =>
        Assert.Equal(new[] { "Create", "Options" },
            ButtonGlyphs.ForCombo(ButtonIconFamily.PlayStation, Menu | View).Select(g => g.Text));

    [Fact]
    public void ForComboShowsShouldersBeforeFaceButtons() =>
        Assert.Equal(new[] { "LB", "RB", "Y" },
            ButtonGlyphs.ForCombo(ButtonIconFamily.Xbox, Y | RightBumper | LeftBumper).Select(g => g.Text));
}
