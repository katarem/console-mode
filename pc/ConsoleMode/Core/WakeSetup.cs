using System.ComponentModel;
using System.Diagnostics;
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
    /// <summary>Dispositivos a los que se ha permitido despertar el PC (no lo tenían permitido).</summary>
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
        "xbox|8bitdo|bluetooth|game ?controller|gamepad|controlador de juegos|mando", RegexOptions.IgnoreCase);

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

                var pnpId = (string?)adapter["PnpId"];
                var device = wakeDevices.FirstOrDefault(d => pnpId != null && d.InstanceId.Equals(pnpId, StringComparison.OrdinalIgnoreCase));
                if (device != null)
                {
                    Arm(state, device);
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
            .Where(d => GamepadDevice.IsMatch(d.Name) && d.Class is not ("Net" or "Keyboard" or "Mouse"))
            .ToList();
        if (candidates.Count == 0)
        {
            state.Results.Add(new WakeSetupResult(false,
                "Mando: no hay ningún adaptador Xbox ni Bluetooth que pueda despertar el PC. Conecta el adaptador y vuelve a aplicar."));
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
        var (ac, dc) = QueryConsoleLock(scheme);
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

        foreach (var instance in state.ArmedDevices)
        {
            Try($"quitar el permiso de despertar a {instance}", () => SetWakeEnabled(instance, false));
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

    private sealed record WakeDevice(string InstanceName, string InstanceId, string Name, string Class, bool Enabled);

    private static List<WakeDevice> ListWakeDevices()
    {
        var items = RunPowerShellJson("""
            $devices = @{}
            Get-PnpDevice -PresentOnly | ForEach-Object { $devices[$_.InstanceId.ToUpperInvariant()] = $_ }
            Get-CimInstance -Namespace root\wmi -ClassName MSPower_DeviceWakeEnable | ForEach-Object {
              $id = $_.InstanceName -replace '_\d+$', ''
              $d = $devices[$id.ToUpperInvariant()]
              if ($d) {
                [pscustomobject]@{ InstanceName = $_.InstanceName; InstanceId = $id; Name = "$($d.FriendlyName)"; Class = "$($d.Class)"; Enabled = [bool]$_.Enable }
              }
            }
            """);
        return items.OfType<JsonObject>()
            .Select(o => new WakeDevice((string?)o["InstanceName"] ?? "", (string?)o["InstanceId"] ?? "", (string?)o["Name"] ?? "",
                (string?)o["Class"] ?? "", (bool?)o["Enabled"] == true))
            .Where(d => d.InstanceName.Length > 0)
            .ToList();
    }

    private static void Arm(WakeSetupState state, WakeDevice device)
    {
        if (device.Enabled || state.ArmedDevices.Contains(device.InstanceName))
        {
            return;
        }
        SetWakeEnabled(device.InstanceName, true);
        state.ArmedDevices.Add(device.InstanceName);
    }

    private static void SetWakeEnabled(string instanceName, bool enabled) => RunPowerShell(
        $"Get-CimInstance -Namespace root\\wmi -ClassName MSPower_DeviceWakeEnable | Where-Object {{ $_.InstanceName -eq {Quote(instanceName)} }} | " +
        $"Set-CimInstance -Property @{{ Enable = ${enabled.ToString().ToLowerInvariant()} }}");

    // =====================================================================================
    // Plan de energía: pedir inicio de sesión al reactivar (CONSOLELOCK)
    // =====================================================================================

    private static string ActiveScheme()
    {
        var output = Run("powercfg.exe", "/getactivescheme");
        var match = Regex.Match(output, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        return match.Success ? match.Value : throw new InvalidOperationException("No se encuentra el plan de energía activo.");
    }

    private static (int Ac, int Dc) QueryConsoleLock(string scheme)
    {
        // La salida está traducida, pero los dos últimos valores hexadecimales son siempre el de corriente y el de batería.
        var values = Regex.Matches(Run("powercfg.exe", $"/query {scheme} {SubNone} {ConsoleLock}"), @"0x([0-9a-fA-F]{8})")
            .Select(m => Convert.ToInt32(m.Groups[1].Value, 16))
            .ToList();
        return values.Count >= 2 ? (values[^2], values[^1]) : throw new InvalidOperationException("No se pudo leer el plan de energía.");
    }

    private static void SetConsoleLock(string scheme, int ac, int dc)
    {
        Run("powercfg.exe", $"/setacvalueindex {scheme} {SubNone} {ConsoleLock} {ac}");
        Run("powercfg.exe", $"/setdcvalueindex {scheme} {SubNone} {ConsoleLock} {dc}");
        if (string.Equals(ActiveScheme(), scheme, StringComparison.OrdinalIgnoreCase))
        {
            Run("powercfg.exe", $"/setactive {scheme}"); // para que el cambio se aplique ya
        }
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private static bool IsSettable(string? value) => value is "Enabled" or "Disabled";

    private static string Quote(string value) => $"'{value.Replace("'", "''")}'";

    private static string FirstLine(string text) => text.Split('\n', 2)[0].Trim();

    private static JsonArray RunPowerShellJson(string script)
    {
        var output = RunPowerShell($"@({script}) | ConvertTo-Json -Compress -Depth 3");
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
        var full = "$ErrorActionPreference = 'Stop'; [Console]::OutputEncoding = [Text.Encoding]::UTF8; " + script;
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
