using ConsoleMode.Core;

namespace ConsoleMode.UI;

/// <summary>
/// What the tray needs from the main window. It is a plain interface, without WPF types, so that starting in the
/// background does not load WPF: the window and its framework are only loaded the first time it is opened.
/// </summary>
public interface IMainWindow
{
    /// <summary>Shows the window (restoring it if minimized) and brings it to the front.</summary>
    void ShowAndActivate();

    /// <summary>The trusted devices changed (a phone was paired or removed).</summary>
    void RefreshDevices();

    /// <summary>A display or an audio device was connected or removed.</summary>
    void RefreshDeviceLists();

    void UpdateModeState(ModeState state);

    /// <summary>The immersive console mode was switched somewhere else (the gamepad overlay).</summary>
    void ShowImmersiveState(bool enabled);

    /// <summary>Closes the window for good (the app is exiting) instead of hiding it.</summary>
    void CloseForExit();
}
