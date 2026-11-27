using System.Diagnostics;
using ConsoleMode.Native;

namespace ConsoleMode.Core;

public sealed record QuickOption(string Id, string Name, bool IsCurrent);

/// <summary>
/// Audio devices, displays and volumes read in one go, so the gamepad overlay can open without asking Windows for them
/// (enumerating audio endpoints takes hundreds of milliseconds). Immutable: a refresh publishes a new instance.
/// </summary>
public sealed record QuickSnapshot(
    IReadOnlyList<QuickOption> Audio,
    IReadOnlyList<QuickOption> Microphones,
    IReadOnlyList<QuickOption> Communications,
    IReadOnlyList<DisplayInfo> Displays,
    VolumeInfo? OutputVolume,
    VolumeInfo? InputVolume)
{
    public static QuickSnapshot Empty { get; } = new(Array.Empty<QuickOption>(), Array.Empty<QuickOption>(),
        Array.Empty<QuickOption>(), Array.Empty<DisplayInfo>(), null, null);

    public IReadOnlyList<QuickOption> DisplayOptions { get; } =
        Displays.Select(d => new QuickOption(d.Id, d.FriendlyName, d.IsPrimary)).ToList();

    /// <summary>Same matching as <see cref="DisplayManager.Find"/>: stable id first, then GDI name.</summary>
    public DisplayInfo? FindDisplay(string? id) => string.IsNullOrEmpty(id)
        ? null
        : Displays.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
          ?? Displays.FirstOrDefault(d => string.Equals(d.GdiName, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The device lists are equal (volumes are not compared).</summary>
    public bool HasSameDevices(QuickSnapshot other) =>
        Audio.SequenceEqual(other.Audio) && Microphones.SequenceEqual(other.Microphones) &&
        Communications.SequenceEqual(other.Communications) && Displays.SequenceEqual(other.Displays);

    public bool HasSameContent(QuickSnapshot other) =>
        HasSameDevices(other) && OutputVolume == other.OutputVolume && InputVolume == other.InputVolume;
}

/// <summary>
/// Cambios rápidos de audio y pantalla principal (los del overlay y la app móvil). Son temporales:
/// no tocan los perfiles de los modos, así que el próximo cambio de modo aplica lo configurado.
/// </summary>
public static class QuickSwitch
{
    /// <summary>
    /// Se dispara (desde cualquier hilo) después de cambiar el audio o la pantalla, y cuando se conecta o se quita un
    /// dispositivo.
    /// </summary>
    public static event Action? Changed;

    /// <summary>Se ha conectado o quitado una pantalla o un dispositivo de audio (lo avisa DeviceWatcher).</summary>
    public static void NotifyDevicesChanged()
    {
        RequestRefresh();
        Changed?.Invoke();
    }

    // ---------- Cache for the gamepad overlay ----------

    private static readonly object SnapshotLock = new();
    private static readonly SemaphoreSlim RefreshGate = new(1, 1);
    private static QuickSnapshot? _snapshot;
    private static long _requestedRefreshes;
    private static long _completedRefreshes;

    /// <summary>Latest cached devices and volumes, or null until the first read finishes.</summary>
    public static QuickSnapshot? Snapshot
    {
        get
        {
            lock (SnapshotLock)
            {
                return _snapshot;
            }
        }
    }

    /// <summary>Raised from a background thread when <see cref="Snapshot"/> gets different content.</summary>
    public static event Action? SnapshotChanged;

    /// <summary>Refreshes the cache in the background without waiting for it.</summary>
    public static void RequestRefresh() =>
        RefreshAsync().ContinueWith(t => Log.Error("Device cache refresh failed", t.Exception),
            TaskContinuationOptions.OnlyOnFaulted);

    /// <summary>
    /// Reads everything again, off the calling thread. Reads never overlap, and concurrent requests share one read; the
    /// returned snapshot was read entirely after this call started.
    /// </summary>
    public static async Task<QuickSnapshot> RefreshAsync()
    {
        var ticket = Interlocked.Increment(ref _requestedRefreshes);
        await RefreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _completedRefreshes) >= ticket && Snapshot is { } fresh)
            {
                return fresh;
            }
            // Every request made until now is served by this read.
            var covered = Volatile.Read(ref _requestedRefreshes);
            var clock = Stopwatch.StartNew();
            var read = await Task.Run(ReadSnapshot).ConfigureAwait(false);
            var changed = Publish(read);
            Volatile.Write(ref _completedRefreshes, covered);
            if (changed)
            {
                Log.Info($"Device cache refreshed in {clock.ElapsedMilliseconds} ms");
                SnapshotChanged?.Invoke();
            }
            return Snapshot!;
        }
        finally
        {
            RefreshGate.Release();
        }
    }

    /// <summary>Each list catches its own errors, so one failing read leaves only that list empty.</summary>
    private static QuickSnapshot ReadSnapshot()
    {
        var audio = AudioOptions();
        var microphones = MicrophoneOptions();
        return new QuickSnapshot(audio, microphones, CommunicationsOptions(), Displays(),
            audio.Count > 0 ? Volume(AudioFlow.Output) : null,
            microphones.Count > 0 ? Volume(AudioFlow.Input) : null);
    }

    /// <summary>Stores the snapshot if its content differs from the current one. Returns whether it did.</summary>
    private static bool Publish(QuickSnapshot read)
    {
        lock (SnapshotLock)
        {
            if (_snapshot != null && _snapshot.HasSameContent(read))
            {
                return false;
            }
            _snapshot = read;
            return true;
        }
    }

    /// <summary>Keeps the cached volume in line with a change made here, without reading the devices again.</summary>
    private static void UpdateCachedVolume(AudioFlow flow, Func<VolumeInfo, VolumeInfo> change)
    {
        lock (SnapshotLock)
        {
            if (_snapshot == null)
            {
                return;
            }
            _snapshot = flow == AudioFlow.Output
                ? _snapshot with { OutputVolume = _snapshot.OutputVolume is { } output ? change(output) : null }
                : _snapshot with { InputVolume = _snapshot.InputVolume is { } input ? change(input) : null };
        }
    }

    public static IReadOnlyList<QuickOption> AudioOptions() => AudioList(AudioManager.GetOutputDevices);

    public static IReadOnlyList<QuickOption> MicrophoneOptions() => AudioList(AudioManager.GetInputDevices);

    public static IReadOnlyList<QuickOption> CommunicationsOptions() => AudioList(AudioManager.GetCommunicationOutputs);

    private static IReadOnlyList<QuickOption> AudioList(Func<IReadOnlyList<AudioDeviceInfo>> devices)
    {
        try
        {
            return devices().Select(a => new QuickOption(a.Id, a.Name, a.IsDefault)).ToList();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron leer los dispositivos de audio", ex);
            return Array.Empty<QuickOption>();
        }
    }

    /// <summary>Volumen actual del sonido o del micrófono, o null si no hay dispositivo.</summary>
    public static VolumeInfo? Volume(AudioFlow flow)
    {
        try
        {
            return AudioManager.GetVolume(flow);
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo leer el volumen", ex);
            return null;
        }
    }

    public static void SetVolume(AudioFlow flow, int percent)
    {
        AudioManager.SetVolume(flow, percent);
        UpdateCachedVolume(flow, v => v with { Percent = percent });
    }

    public static void SetMuted(AudioFlow flow, bool muted)
    {
        AudioManager.SetMuted(flow, muted);
        UpdateCachedVolume(flow, v => v with { Muted = muted });
    }

    public static IReadOnlyList<QuickOption> DisplayOptions() =>
        Displays().Select(d => new QuickOption(d.Id, d.FriendlyName, d.IsPrimary)).ToList();

    private static IReadOnlyList<DisplayInfo> Displays()
    {
        try
        {
            return DisplayManager.GetDisplays();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron leer las pantallas", ex);
            return Array.Empty<DisplayInfo>();
        }
    }

    public static void SetAudio(string id, string origin)
    {
        AudioManager.SetDefaultOutput(id);
        Log.Info($"{origin}: audio cambiado (temporal)");
        RequestRefresh();
        Changed?.Invoke();
    }

    public static void SetMicrophone(string id, string origin)
    {
        AudioManager.SetDefaultInput(id);
        Log.Info($"{origin}: micrófono cambiado (temporal)");
        RequestRefresh();
        Changed?.Invoke();
    }

    public static void SetCommunications(string id, string origin)
    {
        AudioManager.SetCommunicationOutput(id);
        Log.Info($"{origin}: salida de comunicaciones cambiada (temporal)");
        RequestRefresh();
        Changed?.Invoke();
    }

    /// <summary>Cambia la pantalla principal y, si el frontal (Big Picture, Playnite...) está abierto, lo lleva a esa pantalla.</summary>
    public static void SetDisplay(string id, string origin)
    {
        DisplayManager.SetPrimary(id);
        Log.Info($"{origin}: pantalla principal cambiada (temporal)");
        if (ConsoleFrontend.IsOpen)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await ConsoleFrontend.EnsureOnDisplayAsync(id, TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    Log.Error($"No se pudo mover {ConsoleFrontend.Name}", ex);
                }
            });
        }
        RequestRefresh();
        Changed?.Invoke();
    }
}
