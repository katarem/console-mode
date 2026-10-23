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

    public static IReadOnlyList<QuickOption> AudioOptions()
    {
        try
        {
            return AudioManager.GetOutputDevices().Select(a => new QuickOption(a.Id, a.Name, a.IsDefault)).ToList();
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron leer los dispositivos de audio", ex);
            return Array.Empty<QuickOption>();
        }
    }

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

    /// <summary>Cambia la pantalla principal y, si Big Picture está abierto, lo lleva a esa pantalla.</summary>
    public static void SetDisplay(string id, string origin)
    {
        DisplayManager.SetPrimary(id);
        Log.Info($"{origin}: pantalla principal cambiada (temporal)");
        if (SteamBigPicture.IsOpen)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await SteamBigPicture.EnsureOnDisplayAsync(id, TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    Log.Error("No se pudo mover Big Picture", ex);
                }
            });
        }
        Changed?.Invoke();
    }
}
