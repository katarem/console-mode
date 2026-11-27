using ConsoleMode.Core;

namespace ConsoleMode.Tests;

public class DeviceOptionsTests
{
    private static readonly DeviceOption Tv = new("tv", "TV del salón", "TV");
    private static readonly DeviceOption Monitor = new("mon", "Monitor · predeterminado", "Monitor");

    [Fact]
    public void NoChangeComesFirst()
    {
        var (items, _) = DeviceOptions.Build(new[] { Tv, Monitor }, null, null, "no conectada");

        Assert.Equal(new[] { DeviceOption.NoChange, Tv, Monitor }, items);
    }

    [Fact]
    public void NoSavedDeviceSelectsNoChange() =>
        Assert.Same(DeviceOption.NoChange, DeviceOptions.Build(new[] { Tv }, null, null, "no conectada").Selected);

    [Fact]
    public void TheSavedDeviceIsSelected() =>
        Assert.Equal(Tv, DeviceOptions.Build(new[] { Monitor, Tv }, "tv", "TV", "no conectada").Selected);

    [Fact]
    public void ASavedDeviceThatIsNotConnectedIsKept()
    {
        var (items, selected) = DeviceOptions.Build(new[] { Monitor }, "tv", "TV", "no conectada");

        Assert.Equal(new DeviceOption("tv", "TV (no conectada)", "TV"), selected);
        Assert.Equal(selected, items[^1]);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public void AMissingDeviceWithoutANameShowsItsId()
    {
        var (_, selected) = DeviceOptions.Build(Array.Empty<DeviceOption>(), "{0.0.1}", null, "no disponible");

        Assert.Equal("{0.0.1} (no disponible)", selected.Label);
        Assert.Equal("{0.0.1}", selected.Name);
    }

    [Fact]
    public void OptionsShowTheirLabel() => Assert.Equal("TV del salón", Tv.ToString());
}
