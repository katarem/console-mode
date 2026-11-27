using System.Buffers.Binary;
using ConsoleMode.Core;

namespace ConsoleMode.Tests;

public class ButtonIconsTests
{
    private static readonly string IconsFolder = FindIconsFolder();

    public static TheoryData<ButtonIconFamily, GamepadButtons, bool> AllIcons()
    {
        var data = new TheoryData<ButtonIconFamily, GamepadButtons, bool>();
        foreach (var family in Enum.GetValues<ButtonIconFamily>())
        {
            foreach (var button in Enum.GetValues<GamepadButtons>().Where(b => b != GamepadButtons.None))
            {
                data.Add(family, button, true);
                data.Add(family, button, false);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllIcons))]
    public void EveryButtonOfEveryFamilyHasAnIconForBothBackgrounds(ButtonIconFamily family, GamepadButtons button, bool dark)
    {
        var path = Path.Combine(IconsFolder, ButtonIcons.FileName(family, button, dark));

        Assert.True(File.Exists(path), $"Missing icon {path}");
        var header = File.ReadAllBytes(path).AsSpan(0, 24);
        Assert.True(header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }), "Not a PNG");
        Assert.Equal(128u, BinaryPrimitives.ReadUInt32BigEndian(header[16..20]));
        Assert.Equal(128u, BinaryPrimitives.ReadUInt32BigEndian(header[20..24]));
    }

    [Fact]
    public void ThereAreNoUnusedIcons()
    {
        var expected = AllIcons().Select(row => ButtonIcons.FileName((ButtonIconFamily)row[0], (GamepadButtons)row[1], (bool)row[2]));

        var present = Directory.GetFiles(IconsFolder, "*.png").Select(Path.GetFileName);

        Assert.Equal(expected.Order(), present.Order()!);
    }

    [Fact]
    public void ResourceNamesMatchTheEmbeddedLogicalNames() =>
        Assert.Equal("ConsoleMode.ButtonIcons.PlayStation.A.light.png",
            ButtonIcons.ResourceName(ButtonIconFamily.PlayStation, GamepadButtons.A, darkBackground: false));

    [Fact]
    public void IconNamesNeedASingleButton() =>
        Assert.Throws<ArgumentException>(() => ButtonIcons.FileName(ButtonIconFamily.Xbox, GamepadButtons.A | GamepadButtons.B, true));

    private static string FindIconsFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "pc", "ConsoleMode", "Assets", "ButtonIcons");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("pc/ConsoleMode/Assets/ButtonIcons not found above the test folder");
    }
}
