using ConsoleMode.Native;

namespace ConsoleMode.Core;

public sealed record QuickOption(string Id, string Name, bool IsCurrent);

/// <summary>
/// Cambios rápidos de audio y pantalla principal (los del overlay y la app móvil). Son temporales:
/// no tocan los perfiles de los modos, así que el próximo cambio de modo aplica lo configurado.
/// </summary>
public static class QuickSwitch
{
    /// <summary>Se dispara (desde cualquier hilo) después de cambiar el audio o la pantalla.</summary>
    public static event Action? Changed;

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

    public static void SetVolume(AudioFlow flow, int percent) => AudioManager.SetVolume(flow, percent);

    public static void SetMuted(AudioFlow flow, bool muted) => AudioManager.SetMuted(flow, muted);

    public static IReadOnlyList<QuickOption> DisplayOptions()
    {
        try
        {
            return DisplayManager.GetDisplays().Select(d => new QuickOption(d.Id, d.FriendlyName, d.IsPrimary)).ToList();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron leer las pantallas", ex);
            return Array.Empty<QuickOption>();
        }
    }

    public static void SetAudio(string id, string origin)
    {
        AudioManager.SetDefaultOutput(id);
        Log.Info($"{origin}: audio cambiado (temporal)");
        Changed?.Invoke();
    }

    public static void SetMicrophone(string id, string origin)
    {
        AudioManager.SetDefaultInput(id);
        Log.Info($"{origin}: micrófono cambiado (temporal)");
        Changed?.Invoke();
    }

    public static void SetCommunications(string id, string origin)
    {
        AudioManager.SetCommunicationOutput(id);
        Log.Info($"{origin}: salida de comunicaciones cambiada (temporal)");
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
        Changed?.Invoke();
    }
}
