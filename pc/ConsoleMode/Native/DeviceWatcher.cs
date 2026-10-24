using ConsoleMode.Core;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace ConsoleMode.Native;

/// <summary>
/// Avisa cuando se conecta o se quita una pantalla o un dispositivo de audio, o cambia el predeterminado (también si
/// se cambia desde Windows). Así la ventana, el overlay y los móviles muestran la lista al día sin reiniciar la app.
/// Los avisos llegan en ráfagas (una TV al encenderse da varios): se agrupan y se avisa una vez.
/// </summary>
public sealed class DeviceWatcher : IMMNotificationClient, IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(800);

    private readonly MMDeviceEnumerator _enumerator;
    private readonly System.Threading.Timer _timer;
    private bool _disposed;

    /// <summary>Se dispara desde un hilo en segundo plano.</summary>
    public event Action? Changed;

    public DeviceWatcher()
    {
        _timer = new System.Threading.Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        _enumerator = new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(this);
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Trigger();

    private void Trigger()
    {
        if (!_disposed)
        {
            _timer.Change(Debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Fire()
    {
        if (_disposed)
        {
            return;
        }
        try
        {
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("Error al actualizar la lista de dispositivos", ex);
        }
    }

    // ---------- IMMNotificationClient (llega en hilos de Windows: aquí solo se programa el aviso) ----------

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, DeviceState newState) => Trigger();

    void IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => Trigger();

    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => Trigger();

    void IMMNotificationClient.OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Trigger();

    // Cambia a menudo (volumen, formato...) y no afecta a las listas.
    void IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        try
        {
            _enumerator.UnregisterEndpointNotificationCallback(this);
        }
        catch
        {
            // Al cerrar la app.
        }
        _enumerator.Dispose();
        _timer.Dispose();
    }
}
