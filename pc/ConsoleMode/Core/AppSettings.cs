using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConsoleMode.Native;

namespace ConsoleMode.Core;

public enum AppMode
{
    Pc,
    Console,
}

public static class AppModeExtensions
{
    public static string ToWire(this AppMode mode) => mode == AppMode.Console ? "console" : "pc";

    public static AppMode? FromWire(string? value) => value switch
    {
        "console" => AppMode.Console,
        "pc" => AppMode.Pc,
        _ => null,
    };

    public static string ToDisplayName(this AppMode mode) => mode == AppMode.Console ? "Consola" : "PC";
}

/// <summary>Pantalla y dispositivos de audio que se aplican al entrar en un modo.</summary>
public sealed class ModeProfile
{
    /// <summary>Identificador estable del monitor (ruta del dispositivo). Null = no cambiar.</summary>
    public string? DisplayId { get; set; }
    /// <summary>Nombre legible guardado para mostrarlo aunque el monitor no esté conectado.</summary>
    public string? DisplayName { get; set; }
    /// <summary>Id de endpoint de audio de Windows. Null = no cambiar.</summary>
    public string? AudioDeviceId { get; set; }
    public string? AudioDeviceName { get; set; }
    /// <summary>Micrófono predeterminado (también el de comunicaciones). Null = no cambiar.</summary>
    public string? MicrophoneId { get; set; }
    public string? MicrophoneName { get; set; }
    /// <summary>Salida de comunicaciones (Discord, Teams...), p. ej. unos auriculares. Null = no cambiar.</summary>
    public string? CommunicationsId { get; set; }
    public string? CommunicationsName { get; set; }
}

public sealed class TrustedDevice
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Token compartido, cifrado con DPAPI para el usuario actual.</summary>
    public string ProtectedToken { get; set; } = "";
    public DateTime AddedAt { get; set; }
    public DateTime? LastSeenAt { get; set; }

    [JsonIgnore]
    public string? Token
    {
        get
        {
            try
            {
                var data = ProtectedData.Unprotect(Convert.FromBase64String(ProtectedToken), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return null;
            }
        }
        set
        {
            var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(value ?? ""), null, DataProtectionScope.CurrentUser);
            ProtectedToken = Convert.ToBase64String(data);
        }
    }
}

public sealed class AppSettings
{
    public const int DefaultPort = 47820;
    public const int DiscoveryPort = 47821;

    public string PcId { get; set; } = Guid.NewGuid().ToString("N");
    public int Port { get; set; } = DefaultPort;
    public bool ShowTrayIcon { get; set; } = true;
    public AppMode CurrentMode { get; set; } = AppMode.Pc;

    public ModeProfile PcProfile { get; set; } = new();
    public ModeProfile ConsoleProfile { get; set; } = new();

    /// <summary>Programa que se abre en la TV en modo consola: Big Picture, Playnite u otro.</summary>
    public FrontendKind Frontend { get; set; } = FrontendKind.SteamBigPicture;
    /// <summary>Playnite.FullscreenApp.exe elegido a mano. Null = buscarlo en su carpeta de instalación.</summary>
    public string? PlaynitePath { get; set; }
    public string? CustomFrontendPath { get; set; }
    public string? CustomFrontendArgs { get; set; }

    // Los nombres JSON son los de cuando solo había Big Picture, para conservar la configuración.
    [JsonPropertyName("LaunchBigPicture")]
    public bool LaunchFrontend { get; set; } = true;
    [JsonPropertyName("MoveBigPictureToConsoleDisplay")]
    public bool MoveFrontendToConsoleDisplay { get; set; } = true;
    [JsonPropertyName("CloseBigPictureOnPcMode")]
    public bool CloseFrontendOnPcMode { get; set; } = true;

    /// <summary>Mantener View + Menú en un mando Xbox abre el overlay de audio y pantalla.</summary>
    public bool GamepadOverlayEnabled { get; set; } = true;

    /// <summary>Minimizar los juegos en pantalla completa exclusiva para poder enseñar el overlay encima.</summary>
    public bool MinimizeExclusiveFullscreen { get; set; } = true;

    /// <summary>
    /// Modo consola inmersivo: en modo consola se ocultan al momento las ventanas que no son del frontal, una tienda de
    /// juegos, un juego o el overlay (Spotify, Discord...). Al volver a modo PC se vuelven a ver.
    /// </summary>
    public bool ImmersiveConsole { get; set; }

    /// <summary>Programas (sin «.exe») que también se dejan ver en el modo inmersivo, junto con lo que abran.</summary>
    public List<string> ImmersiveAllowedPrograms { get; set; } = new();

    /// <summary>Buscar versiones nuevas en GitHub al arrancar y cada 12 horas.</summary>
    public bool CheckForUpdates { get; set; } = true;

    public List<TrustedDevice> TrustedDevices { get; set; } = new();

    public ModeProfile ProfileFor(AppMode mode) => mode == AppMode.Console ? ConsoleProfile : PcProfile;

    // ---------- Persistencia ----------

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ConsoleMode");

    public static string FilePath => Path.Combine(Directory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly object FileLock = new();

    public static AppSettings Load()
    {
        lock (FileLock)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo leer la configuración, se usará la de por defecto", ex);
            }

            var fresh = new AppSettings();
            fresh.Save();
            return fresh;
        }
    }

    public void Save()
    {
        lock (FileLock)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}
