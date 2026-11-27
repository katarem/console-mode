using ConsoleMode.Core;
using static ConsoleMode.Core.GamepadButtons;

namespace ConsoleMode.Tests;

public class OverlayCombosTests
{
    [Theory]
    [InlineData(View, ComboValidation.TooFewButtons)]
    [InlineData(View | Menu, ComboValidation.Valid)]
    [InlineData(LeftBumper | RightBumper | Y, ComboValidation.Valid)]
    [InlineData(LeftBumper | RightBumper | X | Y, ComboValidation.TooManyButtons)]
    [InlineData(DPadUp | A, ComboValidation.OnlyNavigationButtons)]
    [InlineData(A | B, ComboValidation.OnlyNavigationButtons)]
    [InlineData(A | X, ComboValidation.Valid)]
    public void ValidatesCombinations(GamepadButtons combo, ComboValidation expected) =>
        Assert.Equal(expected, OverlayCombos.Validate(combo));

    [Fact]
    public void NamesRoundTrip()
    {
        var names = OverlayCombos.ToNames(LeftTrigger | RightBumper | Y);

        Assert.Equal(new[] { "RightBumper", "Y", "LeftTrigger" }, names);
        Assert.Equal(LeftTrigger | RightBumper | Y, OverlayCombos.FromNames(names));
    }

    [Fact]
    public void NamesIgnoreCase() => Assert.Equal(View | Menu, OverlayCombos.FromNames(new[] { "view", " MENU " }));

    [Fact]
    public void UnknownNumericAndCompositeNamesAreSkipped() =>
        Assert.Equal(X | Y, OverlayCombos.FromNames(new[] { "X", "Foo", "16", "Menu, View", "Y" }));

    [Theory]
    [InlineData("Foo")]
    [InlineData("A")]
    [InlineData("A", "B")]
    [InlineData]
    public void InvalidStoredCombosFallBackToTheDefault(params string[] names) =>
        Assert.Equal(OverlayCombos.Default, OverlayCombos.FromNames(names));

    [Fact]
    public void MissingComboFallsBackToTheDefault() => Assert.Equal(View | Menu, OverlayCombos.FromNames(null));
}
