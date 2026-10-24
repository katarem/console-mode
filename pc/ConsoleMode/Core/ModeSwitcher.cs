using ConsoleMode.Native;

namespace ConsoleMode.Core;

public sealed record ModeState(AppMode Mode, bool Busy, string? Error = null);

/// <summary>Aplica los perfiles de pantalla/audio y abre o cierra el frontal (Big Picture, Playnite...) al cambiar de modo.</summary>
public sealed class ModeSwitcher
{
    private readonly AppSettings _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile bool _busy;

    public ModeSwitcher(AppSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Se dispara desde un hilo en segundo plano cada vez que cambia el modo o el estado "ocupado".</summary>
    public event Action<ModeState>? StateChanged;

    public ModeState State => new(_settings.CurrentMode, _busy);

    public Task SwitchAsync(AppMode target) => Task.Run(() => SwitchCoreAsync(target));

    private async Task SwitchCoreAsync(AppMode target)
    {
        await _gate.WaitAsync();
        try
        {
            _busy = true;
            StateChanged?.Invoke(new ModeState(_settings.CurrentMode, true));
            Log.Info($"Cambiando a modo {target.ToDisplayName()}");

            var errors = new List<string>();
            if (target == AppMode.Console)
            {
                await EnterConsoleAsync(errors);
            }
            else
            {
                await EnterPcAsync(errors);
            }

            lock (_settings)
            {
                _settings.CurrentMode = target;
                _settings.Save();
            }
            _busy = false;

            var error = errors.Count > 0 ? string.Join(" ", errors) : null;
            if (error != null)
            {
                Log.Error(error);
            }
            StateChanged?.Invoke(new ModeState(target, false, error));
        }
        catch (Exception ex)
        {
            Log.Error("Error inesperado al cambiar de modo", ex);
            _busy = false;
            StateChanged?.Invoke(new ModeState(_settings.CurrentMode, false, ex.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnterConsoleAsync(List<string> errors)
    {
        var profile = _settings.ConsoleProfile;
        await ApplyProfileAsync(profile, errors);

        if (!_settings.LaunchFrontend)
        {
            return;
        }

        // Damos tiempo a Windows para asentar la nueva disposición de pantallas antes de abrir el frontal.
        if (!string.IsNullOrEmpty(profile.DisplayId))
        {
            await Task.Delay(1500);
        }

        try
        {
            ConsoleFrontend.Open();
        }
        catch (Exception ex)
        {
            errors.Add($"No se pudo abrir {ConsoleFrontend.Name}: {ex.Message}");
            return;
        }

        if (_settings.MoveFrontendToConsoleDisplay && !string.IsNullOrEmpty(profile.DisplayId))
        {
            // No bloqueamos el cambio de modo esperando al frontal (puede tardar si estaba cerrado).
            var displayId = profile.DisplayId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await ConsoleFrontend.EnsureOnDisplayAsync(displayId, TimeSpan.FromSeconds(60));
                }
                catch (Exception ex)
                {
                    Log.Error($"No se pudo recolocar {ConsoleFrontend.Name}", ex);
                }
            });
        }
    }

    private async Task EnterPcAsync(List<string> errors)
    {
        if (_settings.CloseFrontendOnPcMode)
        {
            try
            {
                await ConsoleFrontend.CloseAsync();
            }
            catch (Exception ex)
            {
                errors.Add($"No se pudo cerrar {ConsoleFrontend.Name}: {ex.Message}");
            }
        }

        await ApplyProfileAsync(_settings.PcProfile, errors);
    }

    /// <summary>
    /// Primero la pantalla y después el audio: la salida HDMI/DisplayPort de una TV a veces
    /// no aparece hasta que su pantalla está activa, así que el audio se reintenta unos segundos.
    /// La salida de comunicaciones va después de la normal, que también cambia la de comunicaciones.
    /// </summary>
    private static async Task ApplyProfileAsync(ModeProfile profile, List<string> errors)
    {
        if (!string.IsNullOrEmpty(profile.DisplayId))
        {
            try
            {
                DisplayManager.SetPrimary(profile.DisplayId);
            }
            catch (Exception ex)
            {
                errors.Add($"Pantalla ({profile.DisplayName}): {ex.Message}");
            }
        }

        await ApplyAudioAsync(profile.AudioDeviceId, profile.AudioDeviceName, "Audio", AudioManager.SetDefaultOutput, errors);
        await ApplyAudioAsync(profile.MicrophoneId, profile.MicrophoneName, "Micrófono", AudioManager.SetDefaultInput, errors);
        await ApplyAudioAsync(profile.CommunicationsId, profile.CommunicationsName, "Comunicaciones",
            AudioManager.SetCommunicationOutput, errors);
    }

    private static async Task ApplyAudioAsync(string? id, string? name, string what, Action<string> apply, List<string> errors)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                apply(id);
                return;
            }
            catch (Exception ex)
            {
                if (attempt >= 8)
                {
                    errors.Add($"{what} ({name}): {ex.Message}");
                    return;
                }
                await Task.Delay(750);
            }
        }
    }
}
