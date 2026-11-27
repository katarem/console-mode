using ConsoleMode.Core;
using ConsoleMode.Native;

namespace ConsoleMode.Tests;

public class AppSettingsTests
{
    // settings.json as written by Console Mode 1.9.0 (token shortened).
    internal const string Json190 = """
        {
          "PcId": "3f1c2b",
          "Port": 47900,
          "ShowTrayIcon": false,
          "CurrentMode": "Console",
          "PcProfile": { "DisplayId": "\\\\?\\DISPLAY#1", "DisplayName": "Monitor", "AudioDeviceId": "{0.0.0}.{a}", "AudioDeviceName": "Auriculares" },
          "ConsoleProfile": { "DisplayId": "\\\\?\\DISPLAY#2", "DisplayName": "TV" },
          "Frontend": "Playnite",
          "PlaynitePath": "C:\\Playnite\\Playnite.FullscreenApp.exe",
          "LaunchBigPicture": false,
          "MoveBigPictureToConsoleDisplay": true,
          "CloseBigPictureOnPcMode": false,
          "GamepadOverlayEnabled": true,
          "MinimizeExclusiveFullscreen": false,
          "ImmersiveConsole": true,
          "ImmersiveAllowedPrograms": [ "Spotify" ],
          "CheckForUpdates": true,
          "TrustedDevices": [ { "Id": "phone-1", "Name": "Pixel", "ProtectedToken": "AQ==", "AddedAt": "2026-10-01T10:00:00" } ]
        }
        """;

    [Fact]
    public void Settings190LoadWithoutLosingValues()
    {
        var settings = AppSettings.FromJson(Json190)!;

        Assert.Equal(47900, settings.Port);
        Assert.False(settings.ShowTrayIcon);
        Assert.Equal(AppMode.Console, settings.CurrentMode);
        Assert.Equal("TV", settings.ConsoleProfile.DisplayName);
        Assert.Equal("Auriculares", settings.PcProfile.AudioDeviceName);
        Assert.Equal(FrontendKind.Playnite, settings.Frontend);
        Assert.False(settings.LaunchFrontend);
        Assert.False(settings.CloseFrontendOnPcMode);
        Assert.True(settings.ImmersiveConsole);
        Assert.Equal(new[] { "Spotify" }, settings.ImmersiveAllowedPrograms);
        Assert.Equal("Pixel", Assert.Single(settings.TrustedDevices).Name);
    }

    [Fact]
    public void LegacyJsonNamesAreKeptWhenSaving()
    {
        var json = new AppSettings { LaunchFrontend = false }.ToJson();

        Assert.Contains("\"LaunchBigPicture\": false", json);
        Assert.Contains("\"MoveBigPictureToConsoleDisplay\"", json);
        Assert.Contains("\"Frontend\": \"SteamBigPicture\"", json);
    }

    [Fact]
    public void RoundTripKeepsTheSettings()
    {
        var original = AppSettings.FromJson(Json190)!;

        var copy = AppSettings.FromJson(original.ToJson())!;

        Assert.Equal(original.Port, copy.Port);
        Assert.Equal(original.PlaynitePath, copy.PlaynitePath);
        Assert.Equal(original.TrustedDevices[0].Id, copy.TrustedDevices[0].Id);
    }
}
