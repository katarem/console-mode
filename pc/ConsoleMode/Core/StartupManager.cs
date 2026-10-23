using Microsoft.Win32;

namespace ConsoleMode.Core;

/// <summary>Arranque con Windows mediante HKCU\...\Run (no requiere permisos de administrador).</summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ConsoleMode";
    public const string BackgroundArg = "--background";

    private static string Command => $"\"{Environment.ProcessPath}\" {BackgroundArg}";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, Command);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Si el exe se ha movido, actualiza la ruta guardada en el registro.</summary>
    public static void RefreshPathIfEnabled()
    {
        if (IsEnabled)
        {
            SetEnabled(true);
        }
    }
}
