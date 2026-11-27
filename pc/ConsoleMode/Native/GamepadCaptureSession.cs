using ConsoleMode.Core;

namespace ConsoleMode.Native;

/// <summary>
/// Reads the controllers while a new overlay combination is captured. The overlay cannot open meanwhile: the shared
/// gamepad watcher is suspended, or, when the overlay is turned off and there is none, a private one is started.
/// </summary>
public sealed class GamepadCaptureSession : IDisposable
{
    private readonly GamepadWatcher _watcher;
    private readonly bool _ownsWatcher;
    private bool _disposed;

    public GamepadCaptureSession(GamepadWatcher? shared)
    {
        if (shared != null)
        {
            _watcher = shared;
        }
        else
        {
            _watcher = new GamepadWatcher();
            _ownsWatcher = true;
        }
        _watcher.TriggerSuspended = true;
        if (_ownsWatcher)
        {
            _watcher.Start();
        }
    }

    public GamepadButtons HeldButtons => _watcher.HeldButtons;

    public bool AnyControllerConnected => _watcher.AnyControllerConnected;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_ownsWatcher)
        {
            _watcher.Dispose();
        }
        else
        {
            _watcher.TriggerSuspended = false;
        }
    }
}
