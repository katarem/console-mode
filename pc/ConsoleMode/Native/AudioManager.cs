using System.Runtime.InteropServices;
using ConsoleMode.Core;
using NAudio.CoreAudioApi;

namespace ConsoleMode.Native;

public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault);

/// <summary>Volumen (0-100) y silencio de un dispositivo de audio.</summary>
public sealed record VolumeInfo(int Percent, bool Muted);

/// <summary>Salidas (altavoces, auriculares, TV) o entradas (micrófonos).</summary>
public enum AudioFlow
{
    Output,
    Input,
}

/// <summary>
/// Lista los dispositivos de audio activos y cambia los predeterminados de Windows.
/// Windows no expone una API pública para esto; se usa la interfaz COM IPolicyConfig,
/// la misma que usa el panel de sonido (y herramientas como SoundSwitch o EarTrumpet).
/// </summary>
/// <remarks>
/// Windows tiene dos predeterminados por cada sentido: el normal (juegos, música, vídeo) y el «de comunicaciones», que
/// usan Discord, Teams o Zoom cuando eligen el dispositivo predeterminado de comunicaciones.
/// </remarks>
public static class AudioManager
{
    public static IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => GetDevices(AudioFlow.Output, Role.Multimedia);

    public static IReadOnlyList<AudioDeviceInfo> GetInputDevices() => GetDevices(AudioFlow.Input, Role.Multimedia);

    /// <summary>Salidas, marcando como predeterminada la de comunicaciones.</summary>
    public static IReadOnlyList<AudioDeviceInfo> GetCommunicationOutputs() => GetDevices(AudioFlow.Output, Role.Communications);

    private static IReadOnlyList<AudioDeviceInfo> GetDevices(AudioFlow flow, Role role)
    {
        using var enumerator = new MMDeviceEnumerator();
        var dataFlow = ToDataFlow(flow);
        var defaultId = DefaultId(enumerator, dataFlow, role);

        var result = new List<AudioDeviceInfo>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(dataFlow, DeviceState.Active))
        {
            using (device)
            {
                result.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, device.ID == defaultId));
            }
        }
        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Salida predeterminada para todo (también para comunicaciones).</summary>
    public static void SetDefaultOutput(string deviceId)
    {
        var device = Require(GetOutputDevices(), deviceId, "El dispositivo de audio configurado no está disponible.");
        SetDefault(deviceId, ERole.eConsole, ERole.eMultimedia, ERole.eCommunications);
        Log.Info($"Salida de audio cambiada a {device.Name}");
    }

    /// <summary>Micrófono predeterminado, también para comunicaciones.</summary>
    public static void SetDefaultInput(string deviceId)
    {
        var device = Require(GetInputDevices(), deviceId, "El micrófono configurado no está disponible.");
        SetDefault(deviceId, ERole.eConsole, ERole.eMultimedia, ERole.eCommunications);
        Log.Info($"Micrófono cambiado a {device.Name}");
    }

    /// <summary>Solo la salida de comunicaciones (Discord, Teams...): el resto del sonido sigue donde estaba.</summary>
    public static void SetCommunicationOutput(string deviceId)
    {
        var device = Require(GetOutputDevices(), deviceId, "El dispositivo de comunicaciones configurado no está disponible.");
        SetDefault(deviceId, ERole.eCommunications);
        Log.Info($"Salida de comunicaciones cambiada a {device.Name}");
    }

    // ---------- Volumen y silencio ----------

    /// <summary>Volumen del dispositivo predeterminado, o null si no hay ninguno.</summary>
    public static VolumeInfo? GetVolume(AudioFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        var id = DefaultId(enumerator, ToDataFlow(flow), Role.Multimedia);
        if (id == null)
        {
            return null;
        }
        using var device = enumerator.GetDevice(id);
        var volume = device.AudioEndpointVolume;
        return new VolumeInfo((int)Math.Round(volume.MasterVolumeLevelScalar * 100), volume.Mute);
    }

    /// <summary>Cambia el volumen. En los micrófonos también el del de comunicaciones, si es otro.</summary>
    public static void SetVolume(AudioFlow flow, int percent)
    {
        var level = Math.Clamp(percent, 0, 100) / 100f;
        ForEachDefault(flow, device => device.AudioEndpointVolume.MasterVolumeLevelScalar = level);
    }

    /// <summary>Silencia o activa. En los micrófonos también el de comunicaciones, para que Discord tampoco oiga.</summary>
    public static void SetMuted(AudioFlow flow, bool muted)
    {
        ForEachDefault(flow, device => device.AudioEndpointVolume.Mute = muted);
        Log.Info($"{(flow == AudioFlow.Input ? "Micrófono" : "Sonido")} {(muted ? "silenciado" : "activado")}");
    }

    private static void ForEachDefault(AudioFlow flow, Action<MMDevice> action)
    {
        using var enumerator = new MMDeviceEnumerator();
        var dataFlow = ToDataFlow(flow);
        var ids = new[] { DefaultId(enumerator, dataFlow, Role.Multimedia) }
            .Concat(flow == AudioFlow.Input ? new[] { DefaultId(enumerator, dataFlow, Role.Communications) } : Array.Empty<string?>())
            .OfType<string>()
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            throw new InvalidOperationException(flow == AudioFlow.Input ? "No hay ningún micrófono." : "No hay ninguna salida de audio.");
        }
        foreach (var id in ids)
        {
            using var device = enumerator.GetDevice(id);
            action(device);
        }
    }

    // ---------- Helpers ----------

    private static DataFlow ToDataFlow(AudioFlow flow) => flow == AudioFlow.Input ? DataFlow.Capture : DataFlow.Render;

    private static string? DefaultId(MMDeviceEnumerator enumerator, DataFlow flow, Role role)
    {
        try
        {
            if (enumerator.HasDefaultAudioEndpoint(flow, role))
            {
                using var device = enumerator.GetDefaultAudioEndpoint(flow, role);
                return device.ID;
            }
        }
        catch
        {
            // Sin dispositivo predeterminado.
        }
        return null;
    }

    private static AudioDeviceInfo Require(IReadOnlyList<AudioDeviceInfo> devices, string deviceId, string missing) =>
        devices.FirstOrDefault(d => d.Id == deviceId) ?? throw new InvalidOperationException(missing);

    private static void SetDefault(string deviceId, params ERole[] roles)
    {
        var policy = (IPolicyConfig)new CPolicyConfigClient();
        try
        {
            foreach (var role in roles)
            {
                var hr = policy.SetDefaultEndpoint(deviceId, role);
                if (hr != 0)
                {
                    throw new InvalidOperationException($"No se pudo cambiar el dispositivo de audio (HRESULT 0x{hr:X8}).");
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(policy);
        }
    }

    private enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2,
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    private class CPolicyConfigClient
    {
    }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isDefault, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int isDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int visible);
    }
}
