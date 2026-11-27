using System.Diagnostics;
using System.Runtime.InteropServices;
using ConsoleMode.Core;

namespace ConsoleMode.Native;

/// <summary>
/// Lee los mandos XInput (Xbox y compatibles) en segundo plano y emite acciones del overlay.
/// XInput se puede consultar sin tener el foco, así que funciona aunque Big Picture o un juego estén delante.
/// </summary>
public sealed class GamepadWatcher : IDisposable
{
    private const int MaxControllers = 4;
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(33);   // ~30 Hz
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromMilliseconds(16); // ~60 Hz con el overlay abierto
    // Consultar un mando desconectado es lento: los huecos vacíos se miran solo cada 2 s.
    private static readonly TimeSpan DisconnectedRetry = TimeSpan.FromSeconds(2);

    private readonly GamepadNavigator[] _navigators = Enumerable.Range(0, MaxControllers).Select(_ => new GamepadNavigator()).ToArray();
    private readonly TimeSpan[] _retryAt = new TimeSpan[MaxControllers];
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;

    /// <summary>Se dispara desde el hilo del mando, con la acción y el índice XInput (0–3) del mando que la ha hecho.</summary>
    public event Action<PadAction, int>? Action;

    /// <summary>Lo actualiza la UI: con el overlay abierto se navega y se lee más a menudo.</summary>
    public volatile bool OverlayOpen;

    /// <summary>Algún mando tiene un botón pulsado o el stick inclinado.</summary>
    public bool AnyInputHeld => _anyInputHeld;

    private volatile bool _anyInputHeld;
    private const short StickHeldThreshold = 16000;

    public void Start()
    {
        if (_thread != null)
        {
            return;
        }
        _thread = new Thread(Loop) { IsBackground = true, Name = "Gamepad", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var now = clock.Elapsed;
                var held = false;
                for (var i = 0; i < MaxControllers; i++)
                {
                    if (now < _retryAt[i])
                    {
                        continue;
                    }

                    if (XInputGetState(i, out var state) != 0)
                    {
                        _retryAt[i] = now + DisconnectedRetry;
                        continue;
                    }

                    var pad = state.Gamepad;
                    held |= pad.wButtons != 0 ||
                            Math.Abs((int)pad.sThumbLX) > StickHeldThreshold || Math.Abs((int)pad.sThumbLY) > StickHeldThreshold;

                    foreach (var action in _navigators[i].Update(pad.wButtons, pad.sThumbLX, pad.sThumbLY, now, OverlayOpen))
                    {
                        Action?.Invoke(action, i);
                    }
                }
                _anyInputHeld = held;

                _cts.Token.WaitHandle.WaitOne(OverlayOpen ? ActiveInterval : IdleInterval);
            }
        }
        catch (DllNotFoundException ex)
        {
            Log.Error("XInput no está disponible; el overlay con mando queda desactivado", ex);
        }
        catch (Exception ex)
        {
            Log.Error("Error leyendo el mando", ex);
        }
    }

    /// <summary>
    /// Battery of the controller at <paramref name="controllerIndex"/>; null when it is unknown, the controller is not
    /// connected or this XInput version cannot tell. Blocking: call it off the UI thread.
    /// </summary>
    public static ControllerBattery? ReadBattery(int controllerIndex)
    {
        try
        {
            return XInputGetBatteryInformation(controllerIndex, BatteryDevTypeGamepad, out var info) == 0
                ? ControllerBattery.FromXInput(info.BatteryType, info.BatteryLevel)
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            if (!_batteryUnavailableLogged)
            {
                _batteryUnavailableLogged = true;
                Log.Error("XInput no permite leer la batería del mando", ex);
            }
            return null;
        }
    }

    private static volatile bool _batteryUnavailableLogged;

    public void Dispose()
    {
        _cts.Cancel();
        _thread?.Join(500);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XINPUT_BATTERY_INFORMATION
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    private const byte BatteryDevTypeGamepad = 0;

    [DllImport("xinput1_4.dll")]
    private static extern int XInputGetState(int userIndex, out XINPUT_STATE state);

    [DllImport("xinput1_4.dll")]
    private static extern int XInputGetBatteryInformation(int userIndex, byte devType, out XINPUT_BATTERY_INFORMATION information);
}
