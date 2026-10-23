using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ConsoleMode.Core;

public sealed record UpdateInfo(Version Version, string AssetName, string DownloadUrl, string? Sha256, string ReleaseUrl);

/// <summary>
/// Actualizaciones desde las releases de GitHub. La versión instalada se actualiza con el instalador (en modo
/// silencioso) y la portable sustituyendo su propio .exe. Cada release puede no traer las dos apps: se busca en las
/// últimas releases el archivo más nuevo para esta versión.
/// </summary>
public static class Updater
{
    public const string Repository = "katarem/console-mode";
    public const string AfterUpdateArg = "--after-update";

    // Mismo AppId que installer/ConsoleMode.iss: Inno Setup guarda la desinstalación en «{AppId}_is1».
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F1E3C52-9B7A-4C1D-8E5F-2A9D4B7C3E10}_is1";

    private static readonly Regex SetupAsset = new(@"^ConsoleMode-Setup-(\d+\.\d+\.\d+)\.exe$", RegexOptions.IgnoreCase);
    private static readonly Regex PortableAsset = new(@"^ConsoleMode-(\d+\.\d+\.\d+)-portable\.exe$", RegexOptions.IgnoreCase);

    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Si este .exe es el que dejó el instalador (y no una copia portable).</summary>
    public static bool IsInstalled
    {
        get
        {
            try
            {
                foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                {
                    using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = root.OpenSubKey(UninstallKey);
                    if (key?.GetValue("InstallLocation") is string location && !string.IsNullOrEmpty(location))
                    {
                        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
                        return string.Equals(Path.GetFullPath(location).TrimEnd('\\'), Path.GetFullPath(exeDir).TrimEnd('\\'),
                            StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("No se pudo comprobar si Console Mode está instalado", ex);
            }
            return false;
        }
    }

    /// <summary>Devuelve la actualización disponible para esta copia, o null si ya está al día.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases?per_page=20", ct);
        var releases = JsonNode.Parse(json) as JsonArray ?? new JsonArray();
        var pattern = IsInstalled ? SetupAsset : PortableAsset;

        UpdateInfo? best = null;
        foreach (var release in releases.OfType<JsonObject>())
        {
            if ((bool?)release["draft"] == true || (bool?)release["prerelease"] == true)
            {
                continue;
            }
            foreach (var asset in (release["assets"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                var name = (string?)asset["name"] ?? "";
                var match = pattern.Match(name);
                if (!match.Success || !Version.TryParse(match.Groups[1].Value, out var version))
                {
                    continue;
                }
                if (best == null || version > best.Version)
                {
                    var digest = (string?)asset["digest"];
                    best = new UpdateInfo(
                        version,
                        name,
                        (string?)asset["browser_download_url"] ?? "",
                        digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : null,
                        (string?)release["html_url"] ?? $"https://github.com/{Repository}/releases");
                }
            }
        }

        return best != null && best.Version > CurrentVersion && IsTrustedUrl(best.DownloadUrl) ? best : null;
    }

    /// <summary>Descarga el archivo de la actualización a una carpeta temporal y comprueba su SHA-256.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<int>? progress, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), "ConsoleMode-update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, update.AssetName);

        using (var response = await Http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0)
                {
                    progress?.Report((int)(done * 100 / total.Value));
                }
            }
        }

        if (update.Sha256 != null)
        {
            await using var file = File.OpenRead(path);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, ct));
            if (!string.Equals(hash, update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidOperationException("El archivo descargado no coincide con el de la release (SHA-256).");
            }
        }

        Log.Info($"Actualización {update.Version} descargada en {path}");
        return path;
    }

    /// <summary>
    /// Aplica la actualización. Devuelve true si la app debe cerrarse ya (el instalador o la nueva copia toman el
    /// relevo). Si el usuario cancela el aviso de administrador del instalador, devuelve false y no cambia nada.
    /// </summary>
    public static bool Apply(UpdateInfo update, string downloadedFile)
    {
        if (SetupAsset.IsMatch(update.AssetName))
        {
            try
            {
                // El instalador cierra esta instancia (--exit), sustituye los archivos y vuelve a abrir la app.
                Process.Start(new ProcessStartInfo(downloadedFile, "/SILENT /SP- /NOCANCEL /NORESTART") { UseShellExecute = true });
                Log.Info($"Instalador de la versión {update.Version} lanzado");
                return true;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                Log.Info("Actualización cancelada en el aviso de administrador");
                return false;
            }
        }

        // Portable: Windows deja renombrar un .exe en uso, así que se aparta el actual y se pone el nuevo en su sitio.
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("No se sabe dónde está el .exe actual.");
        var old = current + ".old";
        if (File.Exists(old))
        {
            File.Delete(old);
        }
        File.Move(current, old);
        try
        {
            File.Move(downloadedFile, current);
        }
        catch
        {
            File.Move(old, current);
            throw;
        }

        Process.Start(new ProcessStartInfo(current, $"{StartupManager.BackgroundArg} {AfterUpdateArg}") { UseShellExecute = false });
        Log.Info($"Versión portable {update.Version} colocada en {current}");
        return true;
    }

    /// <summary>Borra el .exe anterior que deja una actualización portable.</summary>
    public static void CleanupAfterUpdate()
    {
        try
        {
            var old = Environment.ProcessPath + ".old";
            if (File.Exists(old))
            {
                File.Delete(old);
            }
        }
        catch
        {
            // Puede seguir en uso unos segundos; se borrará en el próximo arranque.
        }
    }

    private static bool IsTrustedUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ConsoleMode", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }
}
