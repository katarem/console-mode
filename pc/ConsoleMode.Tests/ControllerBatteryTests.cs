using ConsoleMode.Core;

namespace ConsoleMode.Tests;

public class ControllerBatteryTests
{
    [Theory]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    [InlineData(0x07)]
    public void UnknownOrDisconnectedTypesGiveNoBattery(byte type) => Assert.Null(ControllerBattery.FromXInput(type, 3));

    [Fact]
    public void WiredControllersAreNeverLow()
    {
        var battery = ControllerBattery.FromXInput(0x01, 0)!.Value;

        Assert.Equal(ControllerPower.Wired, battery.Power);
        Assert.False(battery.IsLow);
        Assert.Equal("Con cable", battery.Label);
    }

    [Theory]
    [InlineData(0, "Vacía", true)]
    [InlineData(1, "Baja", true)]
    [InlineData(2, "Media", false)]
    [InlineData(3, "Llena", false)]
    [InlineData(9, "Llena", false)]
    public void BatteryLevelsMapToBarsAndLabels(byte level, string label, bool low)
    {
        var battery = ControllerBattery.FromXInput(0x02, level)!.Value;

        Assert.Equal(ControllerPower.Battery, battery.Power);
        Assert.Equal(label, battery.Label);
        Assert.Equal(low, battery.IsLow);
    }
}
