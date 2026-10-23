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

    /// <summary>Se dispara desde el hilo del mando.</summary>
    public event Action<PadAction>? Action;

    /// <summary>Lo actualiza la UI: con el overlay abierto se navega y se lee más a menudo.</summary>
    public volatile bool OverlayOpen;

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

                    foreach (var action in _navigators[i].Update(state.Gamepad.wButtons, state.Gamepad.sThumbLY, now, OverlayOpen))
                    {
                        Action?.Invoke(action);
                    }
                }

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

    [DllImport("xinput1_4.dll")]
    private static extern int XInputGetState(int userIndex, out XINPUT_STATE state);
}
