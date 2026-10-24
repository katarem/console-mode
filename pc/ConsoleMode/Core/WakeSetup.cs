using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ConsoleMode.Core;

/// <summary>Qué ajustes de Windows prepara Console Mode para encender el PC a distancia.</summary>
public sealed record WakeSetupOptions(bool Network, bool Gamepad, bool DisableFastStartup, bool NoSignInOnWake)
{
    public static readonly WakeSetupOptions Default = new(true, true, true, false);

    public string ToArg() => string.Join(",", new[]
    {
        Network ? "network" : null,
        Gamepad ? "gamepad" : null,
        DisableFastStartup ? "faststartup" : null,
        NoSignInOnWake ? "signin" : null,
    }.Where(x => x != null));

    public static WakeSetupOptions FromArg(string? arg)
    {
        var parts = (arg ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new WakeSetupOptions(parts.Contains("network"), parts.Contains("gamepad"), parts.Contains("faststartup"),
            parts.Contains("signin"));
    }
}

/// <summary>Resultado de un paso, para mostrarlo en la ventana.</summary>
public sealed record WakeSetupResult(bool Ok, string Message);

/// <summary>Adaptador de red tal y como estaba antes de tocarlo.</summary>
public sealed record AdapterBackup(string Name, string Description, string? WakeOnMagicPacket, string? WakeOnPattern);

/// <summary>Lo que Console Mode ha cambiado y cómo estaba antes, para poder deshacerlo.</summary>
public sealed class WakeSetupState
{
    public DateTime AppliedAt { get; set; }
    public WakeSetupOptions Options { get; set; } = WakeSetupOptions.Default;
    /// <summary>
    /// Dispositivos a los que se ha permitido despertar el PC (no lo tenían permitido), por su nombre en powercfg. Las
    /// versiones 1.5.0 y 1.5.1 guardaban la ruta del dispositivo (con «\»).
    /// </summary>
    public List<string> ArmedDevices { get; set; } = new();
    public List<AdapterBackup> Adapters { get; set; } = new();
    /// <summary>Se ha desactivado el inicio rápido. Valor anterior de HiberbootEnabled (null = no existía).</summary>
    public bool FastStartupChanged { get; set; }
    public int? FastStartupPrevious { get; set; }
    /// <summary>Se ha quitado el inicio de sesión al reactivar. Plan de energía y valores anteriores (CA y batería).</summary>
    public string? ConsoleLockScheme { get; set; }
    public int ConsoleLockPreviousAc { get; set; }
    public int ConsoleLockPreviousDc { get; set; }
    public List<WakeSetupResult> Results { get; set; } = new();
}

/// <summary>
/// Prepara Windows para encender el PC desde el móvil (Wake-on-LAN) y, si el hardware lo permite, despertarlo con el
/// mando. Cada cambio se guarda con su valor anterior en %ProgramData%\ConsoleMode\wake-setup.json y se puede deshacer.
/// Los cambios necesitan permisos de administrador: la app se relanza a sí misma elevada con <see cref="Arg"/>.
/// </summary>
public static class WakeSetup
{
    public const string Arg = "--wake-setup";

    private const string PowerKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Power";
    private const string SubNone = "fea3413e-7e05-4911-9a71-700331f1c294";
    private const string ConsoleLock = "0e796bdb-100d-47d6-a2d5-f7d2daa51f51";

    // Adaptadores que pueden despertar el PC al pulsar el botón Xbox: el adaptador inalámbrico de Xbox, la radio
    // Bluetooth y los mandos con cable o receptor propio.
    private static readonly Regex GamepadDevice = new(
        "xbox|8bitdo|bluetooth|game ?controller|gamepad|controlador de juegos|mando|xinput", RegexOptions.IgnoreCase);

    // Dispositivos que encajan por nombre pero no son el mando ni su receptor (p. ej. la red por Bluetooth).
    private static readonly Regex NotGamepadDevice = new(
        "keyboard|mouse|teclado|rat[oó]n|personal area network|red de [aá]rea personal", RegexOptions.IgnoreCase);

    public static string StateFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ConsoleMode", "wake-setup.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Lo aplicado ahora mismo, o null si no hay nada configurado.</summary>
    public static WakeSetupState? LoadState()
    {
        try
        {
            return File.Exists(StateFile)
                ? JsonSerializer.Deserialize<WakeSetupState>(File.ReadAllText(StateFile), JsonOptions)
                : null;
        }
        catch (Exception ex)
        {
            Log.Error("No se pudo leer la configuración de encendido", ex);
            return null;
        }
    }

    // =====================================================================================
    // Desde la app (sin permisos): relanza el .exe como administrador
    // =====================================================================================

    /// <summary>Aplica las opciones. Devuelve false si se cancela el aviso de administrador.</summary>
    public static Task<bool> ApplyElevatedAsync(WakeSetupOptions options) => RunElevatedAsync($"apply {options.ToArg()}");

    /// <summary>Deshace todo lo aplicado. Devuelve false si se cancela el aviso de administrador.</summary>
    public static Task<bool> RevertElevatedAsync() => RunElevatedAsync("revert");

    private static async Task<bool> RunElevatedAsync(string args)
    {
        Process? process;
        try
        {
            process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"{Arg} {args}")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Info("Configuración de encendido cancelada en el aviso de administrador");
            return false;
        }
        if (process == null)
        {
            return false;
        }
        using (process)
        {
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException("No se pudo completar la configuración. Mira el registro de Console Mode.");
            }
        }
        return true;
    }

    // =====================================================================================
    // Proceso elevado
    // =====================================================================================

    /// <summary>Punto de entrada de «ConsoleMode.exe --wake-setup apply|revert [opciones]». Devuelve el código de salida.</summary>
    public static int RunFromCommandLine(string[] args)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, Arg, StringComparison.OrdinalIgnoreCase));
        var command = index + 1 < args.Length ? args[index + 1] : "";
        try
        {
            switch (command.ToLowerInvariant())
            {
                case "apply":
                    Apply(WakeSetupOptions.FromArg(index + 2 < args.Length ? args[index + 2] : ""));
                    return 0;
                case "revert":
                    Revert();
                    return 0;
                default:
                    Log.Error($"{Arg}: orden desconocida «{command}»");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"{Arg} {command}: error", ex);
            return 1;
        }
    }

    private static void Apply(WakeSetupOptions options)
    {
        // Se parte siempre de cómo estaba Windows: así cambiar las opciones no pierde los valores originales.
        Revert();

        var state = new WakeSetupState { AppliedAt = DateTime.Now, Options = options };
        List<WakeDevice> wakeDevices;
        try
        {
            wakeDevices = ListWakeDevices();
        }
        catch (Exception ex)
        {
            Log.Error("Encendido a distancia: no se pudo leer qué dispositivos pueden despertar el PC", ex);
            wakeDevices = new List<WakeDevice>();
            state.Results.Add(new WakeSetupResult(false,
                $"No se pudo leer qué dispositivos pueden despertar el PC ({FirstLine(ex.Message)})."));
        }

        if (options.Network)
        {
            Step(state, "Tarjeta de red", () => ApplyNetwork(state, wakeDevices));
        }
        if (options.Gamepad)
        {
            Step(state, "Mando", () => ApplyGamepad(state, wakeDevices));
        }
        if (options.DisableFastStartup)
        {
            Step(state, "Inicio rápido", () => ApplyFastStartup(state));
        }
        if (options.NoSignInOnWake)
        {
            Step(state, "Inicio de sesión", () => ApplyNoSignIn(state));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
        File.WriteAllText(StateFile, JsonSerializer.Serialize(state, JsonOptions));
        Log.Info($"Encendido a distancia configurado: {options.ToArg()}");
    }

    private static void Step(WakeSetupState state, string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"Encendido a distancia: {name}", ex);
            state.Results.Add(new WakeSetupResult(false, $"{name}: no se pudo configurar ({FirstLine(ex.Message)})."));
        }
    }

    private static void ApplyNetwork(WakeSetupState state, List<WakeDevice> wakeDevices)
    {
        var adapters = RunPowerShellJson("""
            Get-NetAdapter -Physical | Where-Object { $_.InterfaceDescription -notmatch 'Bluetooth' } | ForEach-Object {
              $pm = Get-NetAdapterPowerManagement -Name $_.Name -ErrorAction SilentlyContinue
              [pscustomobject]@{
                Name = $_.Name; Description = $_.InterfaceDescription; PnpId = $_.PnPDeviceID
                Wired = ($_.PhysicalMediaType -match '802\.3'); Up = ($_.Status -eq 'Up')
                Magic = "$($pm.WakeOnMagicPacket)"; Pattern = "$($pm.WakeOnPattern)"
              }
            }
            """);

        if (adapters.Count == 0)
        {
            state.Results.Add(new WakeSetupResult(false, "Tarjeta de red: no se ha encontrado ninguna tarjeta física."));
            return;
        }

        foreach (var adapter in adapters.OfType<JsonObject>())
        {
            var name = (string?)adapter["Name"] ?? "";
            var description = (string?)adapter["Description"] ?? name;
            var magic = (string?)adapter["Magic"];
            var pattern = (string?)adapter["Pattern"];
            var wired = (bool?)adapter["Wired"] == true;
            if (!IsSettable(magic))
            {
                if (wired)
                {
                    state.Results.Add(new WakeSetupResult(false, $"«{name}» ({description}) no admite Wake-on-LAN."));
                }
                continue;
            }

            try
            {
                // Solo el paquete mágico despierta el PC: con «patrones» cualquier tráfico de la red podría hacerlo.
                if (magic != "Enabled" || pattern == "Enabled")
                {
                    var set = $"Set-NetAdapterPowerManagement -Name {Quote(name)} -WakeOnMagicPacket Enabled" +
                              (IsSettable(pattern) ? " -WakeOnPattern Disabled" : "");
                    state.Adapters.Add(new AdapterBackup(name, description, magic, IsSettable(pattern) ? pattern : null));
                    RunPowerShell(set);
                }

                // «Permitir que este dispositivo reactive el equipo»: powercfg nombra la tarjeta por su descripción.
                var device = wakeDevices.FirstOrDefault(d => d.Name.Equals(description, StringComparison.OrdinalIgnoreCase));
                if (device != null)
                {
                    Arm(state, device);
                }
                else
                {
                    Log.Info($"Encendido a distancia: «{description}» no aparece en powercfg /devicequery wake_programmable");
                }

                state.Results.Add(new WakeSetupResult(true, wired
                    ? $"«{name}» ({description}): Wake-on-LAN activado."
                    : $"«{name}» ({description}): Wake-on-LAN activado. Por Wi-Fi rara vez funciona; mejor por cable."));
            }
            catch (Exception ex)
            {
                Log.Error($"Wake-on-LAN en {name}", ex);
                state.Results.Add(new WakeSetupResult(false, $"«{name}»: no se pudo activar Wake-on-LAN ({FirstLine(ex.Message)})."));
            }
        }

        if (!adapters.OfType<JsonObject>().Any(a => (bool?)a["Wired"] == true))
        {
            state.Results.Add(new WakeSetupResult(false,
                "No hay tarjeta de red por cable. Wake-on-LAN casi siempre necesita que el PC esté conectado por cable."));
        }
    }

    private static void ApplyGamepad(WakeSetupState state, List<WakeDevice> wakeDevices)
    {
        var candidates = wakeDevices
            .Where(d => GamepadDevice.IsMatch(d.Name) && !NotGamepadDevice.IsMatch(d.Name))
            .ToList();
        if (candidates.Count == 0)
        {
            var connected = ConnectedGamepadAdapters();
            state.Results.Add(new WakeSetupResult(false, connected.Count > 0
                ? $"Mando: Windows no deja que {string.Join(", ", connected.Select(n => $"«{n}»"))} despierte el PC (el adaptador " +
                  "o el puerto USB no lo admiten). Prueba en otro puerto USB, mejor uno de la placa base, y vuelve a aplicar."
                : "Mando: no hay ningún adaptador Xbox ni Bluetooth que pueda despertar el PC. Conecta el adaptador y vuelve a aplicar."));
            return;
        }

        foreach (var device in candidates)
        {
            Arm(state, device);
        }
        state.Results.Add(new WakeSetupResult(true,
            $"Mando: pueden despertar el PC {string.Join(", ", candidates.Select(d => $"«{d.Name}»").Distinct())}. " +
            "Funciona desde suspensión si tu hardware lo admite; desde apagado no."));
    }

    /// <summary>Adaptadores del mando conectados (aunque Windows no les deje despertar el PC), para explicar el resultado.</summary>
    private static List<string> ConnectedGamepadAdapters()
    {
        try
        {
            return RunPowerShellJson("""
                Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
                  Where-Object { $_.FriendlyName -match 'xbox|8bitdo' } |
                  ForEach-Object { "$($_.FriendlyName)" }
                """)
                .Select(n => n?.GetValue<string>() ?? "")
                .Where(n => n.Length > 0 && !NotGamepadDevice.IsMatch(n))
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error("Encendido a distancia: no se pudieron listar los adaptadores del mando", ex);
            return new List<string>();
        }
    }

    private static void ApplyFastStartup(WakeSetupState state)
    {
        using var key = Registry.LocalMachine.OpenSubKey(PowerKey, writable: true)
                        ?? throw new InvalidOperationException("No existe la clave de energía del registro.");
        var previous = key.GetValue("HiberbootEnabled") as int?;
        if (previous is 0)
        {
            state.Results.Add(new WakeSetupResult(true, "Inicio rápido: ya estaba desactivado."));
            return;
        }
        state.FastStartupChanged = true;
        state.FastStartupPrevious = previous;
        key.SetValue("HiberbootEnabled", 0, RegistryValueKind.DWord);
        state.Results.Add(new WakeSetupResult(true, "Inicio rápido desactivado: el PC se puede encender desde apagado."));
    }

    private static void ApplyNoSignIn(WakeSetupState state)
    {
        var scheme = ActiveScheme();
        var (ac, dc) = ReadConsoleLock(scheme);
        if (ac == 0 && dc == 0)
        {
            state.Results.Add(new WakeSetupResult(true, "Inicio de sesión: Windows ya no lo pedía al reactivar."));
            return;
        }
        state.ConsoleLockScheme = scheme;
        state.ConsoleLockPreviousAc = ac;
        state.ConsoleLockPreviousDc = dc;
        SetConsoleLock(scheme, 0, 0);
        state.Results.Add(new WakeSetupResult(true,
            "Inicio de sesión: Windows ya no pedirá el PIN al volver de la suspensión (sí al encender o reiniciar)."));
    }

    private static void Revert()
    {
        var state = LoadState();
        if (state == null)
        {
            return;
        }

        foreach (var adapter in state.Adapters)
        {
            Try($"restaurar {adapter.Name}", () =>
            {
                var args = new StringBuilder($"Set-NetAdapterPowerManagement -Name {Quote(adapter.Name)}");
                if (IsSettable(adapter.WakeOnMagicPacket))
                {
                    args.Append($" -WakeOnMagicPacket {adapter.WakeOnMagicPacket}");
                }
                if (IsSettable(adapter.WakeOnPattern))
                {
                    args.Append($" -WakeOnPattern {adapter.WakeOnPattern}");
                }
                RunPowerShell(args.ToString());
            });
        }

        foreach (var device in state.ArmedDevices)
        {
            Try($"quitar el permiso de despertar a {device}", () =>
            {
                if (device.Contains('\\'))
                {
                    SetWakeEnabledByInstance(device, false); // guardado por la 1.5.0 / 1.5.1
                }
                else
                {
                    SetWakeEnabled(device, false);
                }
            });
        }

        if (state.FastStartupChanged)
        {
            Try("restaurar el inicio rápido", () =>
            {
                using var key = Registry.LocalMachine.OpenSubKey(PowerKey, writable: true);
                if (state.FastStartupPrevious is { } value)
                {
                    key?.SetValue("HiberbootEnabled", value, RegistryValueKind.DWord);
                }
                else
                {
                    key?.DeleteValue("HiberbootEnabled", throwOnMissingValue: false);
                }
            });
        }

        if (state.ConsoleLockScheme != null)
        {
            Try("restaurar el inicio de sesión al reactivar",
                () => SetConsoleLock(state.ConsoleLockScheme, state.ConsoleLockPreviousAc, state.ConsoleLockPreviousDc));
        }

        File.Delete(StateFile);
        Log.Info("Encendido a distancia: ajustes de Windows restaurados");
    }

    private static void Try(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error($"Encendido a distancia: no se pudo {what}", ex);
        }
    }

    // =====================================================================================
    // Dispositivos que pueden despertar el PC («Permitir que este dispositivo reactive el equipo»)
    // =====================================================================================

    private sealed record WakeDevice(string Name, bool Enabled);

    /// <summary>
    /// Lo mismo que «Permitir que este dispositivo reactive el equipo» del Administrador de dispositivos, con powercfg.
    /// La salida de powercfg se lee antes de pasar la consola a UTF-8, para no estropear los nombres con tildes.
    /// </summary>
    private static List<WakeDevice> ListWakeDevices()
    {
        var items = RunPowerShellJson("""
            $armed = @(powercfg /devicequery wake_armed) | ForEach-Object { "$_".Trim() } | Where-Object { $_ }
            @(powercfg /devicequery wake_programmable) | ForEach-Object { "$_".Trim() } | Where-Object { $_ } |
              Sort-Object -Unique |
              ForEach-Object { [pscustomobject]@{ Name = $_; Enabled = ($armed -contains $_) } }
            """);
        var devices = items.OfType<JsonObject>()
            .Select(o => new WakeDevice((string?)o["Name"] ?? "", (bool?)o["Enabled"] == true))
            .Where(d => d.Name.Length > 0)
            .ToList();
        Log.Info("Encendido a distancia: pueden despertar el PC: " +
                 string.Join(" · ", devices.Select(d => d.Enabled ? $"{d.Name} (activado)" : d.Name)));
        return devices;
    }

    private static void Arm(WakeSetupState state, WakeDevice device)
    {
        if (device.Enabled || state.ArmedDevices.Contains(device.Name))
        {
            return;
        }
        SetWakeEnabled(device.Name, true);
        state.ArmedDevices.Add(device.Name);
    }

    private static void SetWakeEnabled(string name, bool enabled) =>
        RunPowerShell($"powercfg {(enabled ? "/deviceenablewake" : "/devicedisablewake")} {Quote(name)}; " +
                      "if ($LASTEXITCODE -ne 0) { throw \"powercfg ha fallado ($LASTEXITCODE)\" }");

    private static void SetWakeEnabledByInstance(string instanceName, bool enabled) => RunPowerShell(
        $"Get-CimInstance -Namespace root\\wmi -ClassName MSPower_DeviceWakeEnable | Where-Object {{ $_.InstanceName -eq {Quote(instanceName)} }} | " +
        $"Set-CimInstance -Property @{{ Enable = ${enabled.ToString().ToLowerInvariant()} }}");

    // =====================================================================================
    // Plan de energía: pedir inicio de sesión al reactivar (CONSOLELOCK)
    // =====================================================================================

    // Con la API de energía y no con «powercfg /query»: CONSOLELOCK es un ajuste oculto y powercfg no lo muestra.
    private static string ActiveScheme()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer), "leer el plan de energía activo");
        try
        {
            return Marshal.PtrToStructure<Guid>(pointer).ToString();
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    private static (int Ac, int Dc) ReadConsoleLock(string scheme)
    {
        var (schemeGuid, sub, setting) = (Guid.Parse(scheme), Guid.Parse(SubNone), Guid.Parse(ConsoleLock));
        Check(PowerReadACValueIndex(IntPtr.Zero, ref schemeGuid, ref sub, ref setting, out var ac), "leer el plan de energía");
        Check(PowerReadDCValueIndex(IntPtr.Zero, ref schemeGuid, ref sub, ref setting, out var dc), "leer el plan de energía");
        return ((int)ac, (int)dc);
    }

    private static void SetConsoleLock(string scheme, int ac, int dc)
    {
        var (schemeGuid, sub, setting) = (Guid.Parse(scheme), Guid.Parse(SubNone), Guid.Parse(ConsoleLock));
        Check(PowerWriteACValueIndex(IntPtr.Zero, ref schemeGuid, ref sub, ref setting, (uint)ac), "cambiar el plan de energía");
        Check(PowerWriteDCValueIndex(IntPtr.Zero, ref schemeGuid, ref sub, ref setting, (uint)dc), "cambiar el plan de energía");
        if (string.Equals(ActiveScheme(), scheme, StringComparison.OrdinalIgnoreCase))
        {
            Check(PowerSetActiveScheme(IntPtr.Zero, ref schemeGuid), "aplicar el plan de energía"); // para que se aplique ya
        }
    }

    private static void Check(uint error, string what)
    {
        if (error != 0)
        {
            throw new InvalidOperationException($"No se pudo {what}: {new Win32Exception((int)error).Message}");
        }
    }

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr rootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
        ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
        ref Guid settingGuid, out uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteACValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
        ref Guid settingGuid, uint value);

    [DllImport("powrprof.dll")]
    private static extern uint PowerWriteDCValueIndex(IntPtr rootPowerKey, ref Guid schemeGuid, ref Guid subGroupGuid,
        ref Guid settingGuid, uint value);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private static bool IsSettable(string? value) => value is "Enabled" or "Disabled";

    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();

    private static JsonArray RunPowerShellJson(string script)
    {
        // La salida se calcula antes de pasar la consola a UTF-8 (powercfg y compañía escriben en la de Windows).
        var output = RunPowerShell($"$__result = @({script}); [Console]::OutputEncoding = [Text.Encoding]::UTF8; " +
                                   "ConvertTo-Json -InputObject $__result -Compress -Depth 3");
        return string.IsNullOrWhiteSpace(output)
            ? new JsonArray()
            : JsonNode.Parse(output) switch
            {
                JsonArray array => array,
                JsonObject single => new JsonArray(single),
                _ => new JsonArray(),
            };
    }

    private static string RunPowerShell(string script)
    {
        var full = "$ErrorActionPreference = 'Stop'; " + script;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
        return Run("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", Encoding.UTF8);
    }

    private static string Run(string file, string args, Encoding? encoding = null)
    {
        using var process = Process.Start(new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding,
        }) ?? throw new InvalidOperationException($"No se pudo ejecutar {file}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill();
            throw new TimeoutException($"{file} no ha terminado a tiempo.");
        }
        if (process.ExitCode != 0)
        {
            var error = stderr.Result.Trim();
            throw new InvalidOperationException(error.Length > 0 ? error : stdout.Result.Trim());
        }
        return stdout.Result;
    }
}
