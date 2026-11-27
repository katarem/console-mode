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

    /// <summary>Raised on the gamepad thread with the action and the XInput index (0–3) of the controller that made it.</summary>
    public event Action<PadAction, int>? Action;

    /// <summary>Lo actualiza la UI: con el overlay abierto se navega y se lee más a menudo.</summary>
    public volatile bool OverlayOpen;

    /// <summary>Algún mando tiene un botón pulsado o el stick inclinado.</summary>
    public bool AnyInputHeld => _anyInputHeld;

    private volatile bool _anyInputHeld;
    private const short StickHeldThreshold = 16000;

    private volatile uint _combo = (uint)OverlayCombos.Default;
    private volatile bool _triggerSuspended;
    private volatile uint _heldButtons;
    private volatile bool _anyControllerConnected;

    /// <summary>Combination that opens the overlay. Applied to every controller on the next reading.</summary>
    public GamepadButtons Combo
    {
        get => (GamepadButtons)_combo;
        set => _combo = (uint)value;
    }

    /// <summary>While true (a new combination is being captured), the controllers emit no actions.</summary>
    public bool TriggerSuspended
    {
        get => _triggerSuspended;
        set => _triggerSuspended = value;
    }

    /// <summary>Buttons held on the first connected controller that has any held in the last reading.</summary>
    public GamepadButtons HeldButtons => (GamepadButtons)_heldButtons;

    /// <summary>Whether a controller answered in the last reading.</summary>
    public bool AnyControllerConnected => _anyControllerConnected;

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
                var heldButtons = GamepadButtons.None;
                var connected = false;
                var combo = Combo;
                var suspended = TriggerSuspended;
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

                    connected = true;
                    var pad = state.Gamepad;
                    var buttons = GamepadInput.FromXInput(pad.wButtons, pad.bLeftTrigger, pad.bRightTrigger);
                    held |= buttons != GamepadButtons.None ||
                            Math.Abs((int)pad.sThumbLX) > StickHeldThreshold || Math.Abs((int)pad.sThumbLY) > StickHeldThreshold;
                    if (heldButtons == GamepadButtons.None)
                    {
                        heldButtons = buttons;
                    }

                    var navigator = _navigators[i];
                    navigator.Combo = combo;
                    navigator.TriggerSuspended = suspended;
                    foreach (var action in navigator.Update(buttons, pad.sThumbLX, pad.sThumbLY, now, OverlayOpen))
                    {
                        Action?.Invoke(action, i);
                    }
                }
                _anyInputHeld = held;
                _heldButtons = (uint)heldButtons;
                _anyControllerConnected = connected;

                _cts.Token.WaitHandle.WaitOne(OverlayOpen || suspended ? ActiveInterval : IdleInterval);
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
                Log.Error("XInput cannot read the controller battery", ex);
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
