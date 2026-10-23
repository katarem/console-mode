using System.Drawing;
using System.Runtime.InteropServices;
using ConsoleMode.Core;

namespace ConsoleMode.Native;

public sealed record DisplayInfo(
    string Id,            // Ruta estable del monitor (\\?\DISPLAY#...) o, si no existe, el nombre GDI
    string GdiName,       // \\.\DISPLAY1 (puede cambiar entre reinicios)
    string FriendlyName,  // "LG TV SSCR2", "DELL U2720Q"...
    Rectangle Bounds,
    bool IsPrimary)
{
    public string Label => $"{FriendlyName} ({Bounds.Width}x{Bounds.Height}){(IsPrimary ? " · principal" : "")}";
}

/// <summary>
/// Enumera los monitores conectados al escritorio y permite cambiar la pantalla principal.
/// Usa la API Display Configuration para obtener nombres e identificadores estables
/// y ChangeDisplaySettingsEx para mover el origen del escritorio al monitor elegido.
/// </summary>
public static class DisplayManager
{
    public static IReadOnlyList<DisplayInfo> GetDisplays()
    {
        var targets = QueryTargetNames();
        var result = new List<DisplayInfo>();

        var device = NewDisplayDevice();
        for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++, device = NewDisplayDevice())
        {
            if ((device.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
            {
                continue;
            }

            var mode = NewDevMode();
            if (!EnumDisplaySettings(device.DeviceName, ENUM_CURRENT_SETTINGS, ref mode))
            {
                continue;
            }

            targets.TryGetValue(device.DeviceName, out var target);
            var friendly = !string.IsNullOrWhiteSpace(target.FriendlyName)
                ? target.FriendlyName
                : FallbackMonitorName(device.DeviceName) ?? device.DeviceName;
            var id = !string.IsNullOrWhiteSpace(target.DevicePath) ? target.DevicePath : device.DeviceName;

            result.Add(new DisplayInfo(
                id,
                device.DeviceName,
                friendly,
                new Rectangle(mode.dmPositionX, mode.dmPositionY, mode.dmPelsWidth, mode.dmPelsHeight),
                (device.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0));
        }

        return result;
    }

    public static DisplayInfo? Find(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }
        var displays = GetDisplays();
        return displays.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? displays.FirstOrDefault(d => string.Equals(d.GdiName, id, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Convierte el monitor indicado en la pantalla principal manteniendo la disposición relativa.</summary>
    public static void SetPrimary(string id)
    {
        var displays = GetDisplays();
        var target = displays.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? displays.FirstOrDefault(d => string.Equals(d.GdiName, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("La pantalla configurada no está conectada o está desactivada.");

        if (target.IsPrimary)
        {
            Log.Info($"{target.FriendlyName} ya es la pantalla principal");
            return;
        }

        // La pantalla principal siempre está en (0,0): desplazamos todas para que la nueva quede en el origen.
        var offsetX = target.Bounds.X;
        var offsetY = target.Bounds.Y;

        foreach (var display in displays.OrderByDescending(d => d.GdiName == target.GdiName))
        {
            var mode = NewDevMode();
            if (!EnumDisplaySettings(display.GdiName, ENUM_CURRENT_SETTINGS, ref mode))
            {
                throw new InvalidOperationException($"No se pudo leer la configuración de {display.FriendlyName}.");
            }

            mode.dmFields = DM_POSITION;
            mode.dmPositionX -= offsetX;
            mode.dmPositionY -= offsetY;

            var flags = CDS_UPDATEREGISTRY | CDS_NORESET;
            if (display.GdiName == target.GdiName)
            {
                flags |= CDS_SET_PRIMARY;
            }

            var r = ChangeDisplaySettingsEx(display.GdiName, ref mode, IntPtr.Zero, flags, IntPtr.Zero);
            if (r != DISP_CHANGE_SUCCESSFUL)
            {
                throw new InvalidOperationException($"Windows rechazó el cambio en {display.FriendlyName} (código {r}).");
            }
        }

        // Aplica todos los cambios pendientes de una vez.
        var apply = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        if (apply != DISP_CHANGE_SUCCESSFUL)
        {
            throw new InvalidOperationException($"No se pudo aplicar la nueva disposición de pantallas (código {apply}).");
        }

        Log.Info($"Pantalla principal cambiada a {target.FriendlyName}");
    }

    // ---------------------------------------------------------------------------------------
    // Display Configuration API: nombre "bonito" (EDID) y ruta estable de cada monitor activo
    // ---------------------------------------------------------------------------------------

    private readonly record struct TargetName(string FriendlyName, string DevicePath);

    private static Dictionary<string, TargetName> QueryTargetNames()
    {
        var map = new Dictionary<string, TargetName>(StringComparer.OrdinalIgnoreCase);
        try
        {
            int err;
            DISPLAYCONFIG_PATH_INFO[] paths;
            DISPLAYCONFIG_MODE_INFO[] modes;
            uint pathCount, modeCount;
            do
            {
                err = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount);
                if (err != 0)
                {
                    return map;
                }
                paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
                modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
                err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
            } while (err == ERROR_INSUFFICIENT_BUFFER);

            if (err != 0)
            {
                return map;
            }

            for (var i = 0; i < pathCount; i++)
            {
                var path = paths[i];

                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
                source.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
                source.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
                source.header.adapterId = path.sourceInfo.adapterId;
                source.header.id = path.sourceInfo.id;
                if (DisplayConfigGetDeviceInfo(ref source) != 0)
                {
                    continue;
                }

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME();
                target.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME;
                target.header.size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>();
                target.header.adapterId = path.targetInfo.adapterId;
                target.header.id = path.targetInfo.id;
                if (DisplayConfigGetDeviceInfo(ref target) != 0)
                {
                    continue;
                }

                // En modo duplicado un mismo origen tiene varios destinos: nos quedamos con el primero.
                map.TryAdd(source.viewGdiDeviceName, new TargetName(target.monitorFriendlyDeviceName, target.monitorDevicePath));
            }
        }
        catch (Exception ex)
        {
            Log.Error("QueryDisplayConfig falló", ex);
        }
        return map;
    }

    private static string? FallbackMonitorName(string gdiName)
    {
        var monitor = NewDisplayDevice();
        return EnumDisplayDevices(gdiName, 0, ref monitor, 0) && !string.IsNullOrWhiteSpace(monitor.DeviceString)
            ? monitor.DeviceString
            : null;
    }

    private static DISPLAY_DEVICE NewDisplayDevice() => new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };

    private static DEVMODE NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

    // ------------------------------------ P/Invoke ------------------------------------

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;
    private const int DM_POSITION = 0x20;
    private const uint CDS_UPDATEREGISTRY = 0x1;
    private const uint CDS_SET_PRIMARY = 0x10;
    private const uint CDS_NORESET = 0x10000000;
    private const int DISP_CHANGE_SUCCESSFUL = 0;
    private const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const uint DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_RATIONAL
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public DISPLAYCONFIG_RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    // Solo necesitamos reservar el tamaño correcto (64 bytes); el contenido no se usa.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO
    {
        [FieldOffset(0)] public uint infoType;
        [FieldOffset(4)] public uint id;
        [FieldOffset(8)] public LUID adapterId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string? lpszDeviceName, IntPtr lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
        ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME requestPacket);
}
