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

    /// <summary>Pantalla configurada para el modo consola (null = «No cambiar»).</summary>
    public string? ConsoleDisplayId => _settings.ConsoleProfile.DisplayId;

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

    private Task EnterConsoleAsync(List<string> errors)
    {
        var profile = _settings.ConsoleProfile;
        // El frontal se abre en cuanto está la pantalla, aunque el audio (p. ej. unos auriculares Bluetooth) siga
        // conectándose.
        return ApplyProfileAsync(profile, errors, afterDisplay: () => OpenFrontendAsync(profile, errors));
    }

    private async Task OpenFrontendAsync(ModeProfile profile, List<string> errors)
    {
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
    /// Cuánto se espera a que aparezcan la pantalla y los dispositivos de audio del modo. Al encender el PC desde el
    /// móvil, el cambio llega nada más arrancar: la TV aún está despertando por HDMI y los auriculares Bluetooth
    /// reconectándose, y Windows rechaza el cambio o no los encuentra. Se reintenta cada segundo hasta este tiempo.
    /// </summary>
    private static readonly TimeSpan DeviceWait = TimeSpan.FromSeconds(40);

    /// <summary>
    /// La pantalla y el audio a la vez, esperando a que aparezcan. Tras la pantalla se llama a
    /// <paramref name="afterDisplay"/> (abrir el frontal) sin esperar al audio. La salida de audio va antes que la de
    /// comunicaciones, porque también cambia la de comunicaciones; el micrófono va por su cuenta.
    /// </summary>
    private static async Task ApplyProfileAsync(ModeProfile profile, List<string> errors, Func<Task>? afterDisplay = null)
    {
        var deadline = DateTime.UtcNow + DeviceWait;

        var output = Task.Run(async () => new[]
        {
            await RetryUntilAsync("Audio", profile.AudioDeviceId, profile.AudioDeviceName, AudioManager.SetDefaultOutput, deadline),
            await RetryUntilAsync("Comunicaciones", profile.CommunicationsId, profile.CommunicationsName,
                AudioManager.SetCommunicationOutput, deadline),
        });
        var microphone = Task.Run(() =>
            RetryUntilAsync("Micrófono", profile.MicrophoneId, profile.MicrophoneName, AudioManager.SetDefaultInput, deadline));

        var displayError = await RetryUntilAsync("Pantalla", profile.DisplayId, profile.DisplayName, DisplayManager.SetPrimary, deadline);
        if (afterDisplay != null)
        {
            await afterDisplay();
        }

        var audioErrors = (await output).Append(await microphone);
        errors.InsertRange(0, new[] { displayError }.Concat(audioErrors).OfType<string>());
    }

    /// <summary>Aplica el cambio; si falla, lo reintenta cada segundo hasta el límite. Devuelve el error final o null.</summary>
    private static async Task<string?> RetryUntilAsync(string what, string? id, string? name, Action<string> apply, DateTime deadline)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }
        var waitLogged = false;
        while (true)
        {
            try
            {
                apply(id);
                if (waitLogged)
                {
                    Log.Info($"{what}: {name} ya está disponible");
                }
                return null;
            }
            catch (Exception ex)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return $"{what} ({name}): {ex.Message}";
                }
                if (!waitLogged)
                {
                    Log.Info($"{what}: {name} aún no está disponible, se espera ({ex.Message})");
                    waitLogged = true;
                }
                await Task.Delay(1000);
            }
        }
    }
}
