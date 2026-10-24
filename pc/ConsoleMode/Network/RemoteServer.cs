using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ConsoleMode.Core;

namespace ConsoleMode.Network;

public sealed record PairRequest(string DeviceId, string DeviceName, string RemoteAddress);

/// <summary>
/// Servidor TCP para la app móvil. Protocolo: un objeto JSON por línea (UTF-8, terminado en \n).
/// Ver docs/PROTOCOL.md.
/// </summary>
public sealed class RemoteServer : IDisposable
{
    /// <summary>
    /// 2: añade get_devices / set_audio / set_display (cambios rápidos temporales).
    /// 3: auth_ok y pair_ok incluyen «wake» (MAC y broadcast para encender el PC con Wake-on-LAN).
    /// 4: devices incluye «mics» y «comms»; set_microphone / set_communications.
    /// </summary>
    public const int ProtocolVersion = 4;
    private const int MaxLineLength = 16 * 1024;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PairingTimeout = TimeSpan.FromSeconds(90);

    private readonly AppSettings _settings;
    private readonly ModeSwitcher _switcher;
    private readonly Func<PairRequest, CancellationToken, Task<bool>> _confirmPairing;
    private readonly SemaphoreSlim _pairingGate = new(1, 1);
    private readonly List<Session> _sessions = new();
    private readonly CancellationTokenSource _cts = new();
    private TcpListener? _listener;

    public RemoteServer(AppSettings settings, ModeSwitcher switcher, Func<PairRequest, CancellationToken, Task<bool>> confirmPairing)
    {
        _settings = settings;
        _switcher = switcher;
        _confirmPairing = confirmPairing;
        _switcher.StateChanged += state =>
        {
            Broadcast(StatusMessage(state));
            if (!state.Busy)
            {
                BroadcastDevices(); // el modo cambia la pantalla y el audio
            }
        };
        QuickSwitch.Changed += BroadcastDevices;
    }

    /// <summary>Se ha añadido un dispositivo de confianza o ha cambiado su última conexión.</summary>
    public event Action? DevicesChanged;

    /// <summary>Un dispositivo de confianza ha pedido un cambio de modo (para avisar en la bandeja).</summary>
    public event Action<string, AppMode>? RemoteModeRequested;

    public int ConnectedDevices
    {
        get
        {
            lock (_sessions)
            {
                return _sessions.Count(s => s.Device != null);
            }
        }
    }

    public void Start()
    {
        _listener = TcpListener.Create(_settings.Port); // IPv4 + IPv6
        _listener.Start();
        Log.Info($"Servidor escuchando en el puerto {_settings.Port}");
        _ = AcceptLoopAsync(_listener, _cts.Token);
    }

    /// <summary>Cierra las conexiones abiertas de un dispositivo que ya no es de confianza.</summary>
    public void DisconnectDevice(string deviceId)
    {
        lock (_sessions)
        {
            foreach (var s in _sessions.Where(s => s.Device?.Id == deviceId))
            {
                s.Close();
            }
        }
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error("Error aceptando conexión", ex);
                continue;
            }

            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverCt)
    {
        client.NoDelay = true;
        var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.MapToIPv4().ToString() ?? "?";
        var session = new Session(client);
        lock (_sessions)
        {
            _sessions.Add(session);
        }

        try
        {
            var nonce = RandomToken(24);
            await session.SendAsync(new JsonObject
            {
                ["type"] = "hello",
                ["version"] = ProtocolVersion,
                ["pcId"] = _settings.PcId,
                ["pcName"] = Environment.MachineName,
                ["nonce"] = nonce,
            });

            while (!serverCt.IsCancellationRequested)
            {
                var timeout = session.PairingInProgress ? PairingTimeout + IdleTimeout : IdleTimeout;
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
                idle.CancelAfter(timeout);

                var line = await session.ReadLineAsync(idle.Token);
                if (line == null)
                {
                    break;
                }

                JsonObject? msg;
                try
                {
                    msg = JsonNode.Parse(line) as JsonObject;
                }
                catch
                {
                    msg = null;
                }
                if (msg == null)
                {
                    await session.SendAsync(Error("Mensaje no válido"));
                    continue;
                }

                await HandleMessageAsync(session, msg, nonce, remote, serverCt);
            }
        }
        catch (OperationCanceledException)
        {
            // Inactividad o cierre del servidor.
        }
        catch (IOException)
        {
            // El móvil se ha desconectado.
        }
        catch (Exception ex)
        {
            Log.Error($"Error en la conexión con {remote}", ex);
        }
        finally
        {
            lock (_sessions)
            {
                _sessions.Remove(session);
            }
            session.Close();
            if (session.Device != null)
            {
                DevicesChanged?.Invoke();
            }
        }
    }

    private async Task HandleMessageAsync(Session session, JsonObject msg, string nonce, string remote, CancellationToken ct)
    {
        var type = (string?)msg["type"];

        if (type == "ping")
        {
            await session.SendAsync(new JsonObject { ["type"] = "pong" });
            return;
        }

        if (session.Device == null)
        {
            switch (type)
            {
                case "auth":
                    await HandleAuthAsync(session, msg, nonce, remote);
                    return;
                case "pair_request":
                    await HandlePairRequestAsync(session, msg, remote, ct);
                    return;
                default:
                    await session.SendAsync(Error("No autenticado"));
                    return;
            }
        }

        switch (type)
        {
            case "get_status":
                await session.SendAsync(StatusMessage(_switcher.State));
                break;

            case "get_devices":
                await session.SendAsync(DevicesMessage());
                break;

            case "set_audio":
            case "set_display":
            case "set_microphone":
            case "set_communications":
                var id = (string?)msg["id"];
                if (string.IsNullOrEmpty(id))
                {
                    await session.SendAsync(Error("Falta el id"));
                    break;
                }
                try
                {
                    var origin = $"Móvil ({session.Device.Name})";
                    Action<string, string> apply = type switch
                    {
                        "set_audio" => QuickSwitch.SetAudio,
                        "set_microphone" => QuickSwitch.SetMicrophone,
                        "set_communications" => QuickSwitch.SetCommunications,
                        _ => QuickSwitch.SetDisplay,
                    };
                    await Task.Run(() => apply(id, origin));
                }
                catch (Exception ex)
                {
                    await session.SendAsync(Error(ex.Message));
                }
                break;

            case "set_mode":
                var mode = AppModeExtensions.FromWire((string?)msg["mode"]);
                if (mode == null)
                {
                    await session.SendAsync(Error("Modo desconocido"));
                    break;
                }
                Log.Info($"{session.Device.Name} solicita modo {mode.Value.ToDisplayName()}");
                RemoteModeRequested?.Invoke(session.Device.Name, mode.Value);
                _ = _switcher.SwitchAsync(mode.Value);
                break;

            default:
                await session.SendAsync(Error($"Tipo de mensaje desconocido: {type}"));
                break;
        }
    }

    private async Task HandleAuthAsync(Session session, JsonObject msg, string nonce, string remote)
    {
        var deviceId = (string?)msg["deviceId"] ?? "";
        var proof = (string?)msg["proof"] ?? "";

        TrustedDevice? device;
        lock (_settings)
        {
            device = _settings.TrustedDevices.FirstOrDefault(d => d.Id == deviceId);
        }

        var token = device?.Token;
        if (device == null || token == null)
        {
            Log.Info($"Autenticación rechazada desde {remote}: dispositivo desconocido");
            await session.SendAsync(new JsonObject { ["type"] = "auth_failed", ["reason"] = "unknown_device" });
            return;
        }

        var expected = Convert.FromHexString(ComputeProof(token, nonce));
        byte[] given;
        try
        {
            given = Convert.FromHexString(proof);
        }
        catch (FormatException)
        {
            given = Array.Empty<byte>();
        }

        if (!CryptographicOperations.FixedTimeEquals(expected, given))
        {
            Log.Info($"Autenticación rechazada desde {remote}: prueba incorrecta para {device.Name}");
            await session.SendAsync(new JsonObject { ["type"] = "auth_failed", ["reason"] = "bad_proof" });
            return;
        }

        lock (_settings)
        {
            device.LastSeenAt = DateTime.Now;
            var newName = Sanitize((string?)msg["deviceName"]);
            if (!string.IsNullOrEmpty(newName))
            {
                device.Name = newName;
            }
            _settings.Save();
        }

        session.Device = device;
        Log.Info($"{device.Name} conectado desde {remote}");
        await session.SendAsync(new JsonObject
        {
            ["type"] = "auth_ok",
            ["pcName"] = Environment.MachineName,
            ["wake"] = WakeTargetsJson(),
        });
        await session.SendAsync(StatusMessage(_switcher.State));
        DevicesChanged?.Invoke();
    }

    private async Task HandlePairRequestAsync(Session session, JsonObject msg, string remote, CancellationToken ct)
    {
        var deviceId = Sanitize((string?)msg["deviceId"]);
        var deviceName = Sanitize((string?)msg["deviceName"]);
        if (string.IsNullOrEmpty(deviceId))
        {
            await session.SendAsync(Error("Falta deviceId"));
            return;
        }
        if (string.IsNullOrEmpty(deviceName))
        {
            deviceName = "Móvil";
        }

        // Solo una petición de emparejamiento a la vez para que nadie pueda inundar el PC de diálogos.
        if (!await _pairingGate.WaitAsync(0, ct))
        {
            await session.SendAsync(new JsonObject { ["type"] = "pair_denied", ["reason"] = "busy" });
            return;
        }

        bool accepted;
        session.PairingInProgress = true;
        try
        {
            await session.SendAsync(new JsonObject { ["type"] = "pair_pending" });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(PairingTimeout);
            try
            {
                accepted = await _confirmPairing(new PairRequest(deviceId, deviceName, remote), timeout.Token);
            }
            catch (OperationCanceledException)
            {
                accepted = false;
            }
        }
        finally
        {
            session.PairingInProgress = false;
            _pairingGate.Release();
        }

        if (!accepted)
        {
            Log.Info($"Emparejamiento rechazado: {deviceName} ({remote})");
            await session.SendAsync(new JsonObject { ["type"] = "pair_denied", ["reason"] = "rejected" });
            return;
        }

        var token = RandomToken(32);
        TrustedDevice device;
        lock (_settings)
        {
            _settings.TrustedDevices.RemoveAll(d => d.Id == deviceId);
            device = new TrustedDevice
            {
                Id = deviceId,
                Name = deviceName,
                AddedAt = DateTime.Now,
                LastSeenAt = DateTime.Now,
                Token = token,
            };
            _settings.TrustedDevices.Add(device);
            _settings.Save();
        }

        session.Device = device;
        Log.Info($"Nuevo dispositivo de confianza: {deviceName} ({remote})");
        await session.SendAsync(new JsonObject
        {
            ["type"] = "pair_ok",
            ["token"] = token,
            ["pcId"] = _settings.PcId,
            ["pcName"] = Environment.MachineName,
            ["wake"] = WakeTargetsJson(),
        });
        await session.SendAsync(StatusMessage(_switcher.State));
        DevicesChanged?.Invoke();
    }

    private static JsonArray WakeTargetsJson()
    {
        try
        {
            return new JsonArray(NetworkInfo.GetWakeTargets()
                .Select(t => (JsonNode)new JsonObject { ["mac"] = t.Mac, ["broadcast"] = t.Broadcast })
                .ToArray());
        }
        catch (Exception ex)
        {
            Log.Error("No se pudieron leer las tarjetas de red para Wake-on-LAN", ex);
            return new JsonArray();
        }
    }

    private void Broadcast(JsonObject message)
    {
        List<Session> targets;
        lock (_sessions)
        {
            targets = _sessions.Where(s => s.Device != null).ToList();
        }
        foreach (var s in targets)
        {
            _ = s.SendAsync(message.DeepClone().AsObject()).ContinueWith(t => s.Close(), TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    /// <summary>Envía la lista de audio y pantallas a los móviles conectados (solo si hay alguno).</summary>
    private void BroadcastDevices()
    {
        if (ConnectedDevices > 0)
        {
            Broadcast(DevicesMessage());
        }
    }

    private static JsonObject DevicesMessage()
    {
        static JsonArray ToJson(IEnumerable<QuickOption> options) => new(options
            .Select(o => (JsonNode)new JsonObject { ["id"] = o.Id, ["name"] = o.Name, ["current"] = o.IsCurrent })
            .ToArray());

        return new JsonObject
        {
            ["type"] = "devices",
            ["audio"] = ToJson(QuickSwitch.AudioOptions()),
            ["mics"] = ToJson(QuickSwitch.MicrophoneOptions()),
            ["comms"] = ToJson(QuickSwitch.CommunicationsOptions()),
            ["displays"] = ToJson(QuickSwitch.DisplayOptions()),
        };
    }

    private static JsonObject StatusMessage(ModeState state) => new()
    {
        ["type"] = "status",
        ["mode"] = state.Mode.ToWire(),
        ["busy"] = state.Busy,
        ["error"] = state.Error,
    };

    private static JsonObject Error(string message) => new() { ["type"] = "error", ["message"] = message };

    /// <summary>HMAC-SHA256(clave = token, mensaje = nonce) en hexadecimal minúsculas.</summary>
    public static string ComputeProof(string token, string nonce)
    {
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(nonce));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    private static string RandomToken(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }
        var clean = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length > 64 ? clean[..64] : clean;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener?.Stop();
        lock (_sessions)
        {
            foreach (var s in _sessions)
            {
                s.Close();
            }
        }
    }

    private sealed class Session
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly byte[] _buffer = new byte[4096];
        private readonly MemoryStream _pending = new();
        private int _bufferPos;
        private int _bufferLen;

        public Session(TcpClient client)
        {
            _client = client;
            _stream = client.GetStream();
        }

        public TrustedDevice? Device { get; set; }
        public volatile bool PairingInProgress;

        public async Task<string?> ReadLineAsync(CancellationToken ct)
        {
            _pending.SetLength(0);
            while (true)
            {
                if (_bufferPos >= _bufferLen)
                {
                    _bufferLen = await _stream.ReadAsync(_buffer, ct);
                    _bufferPos = 0;
                    if (_bufferLen == 0)
                    {
                        return null;
                    }
                }

                var newline = Array.IndexOf(_buffer, (byte)'\n', _bufferPos, _bufferLen - _bufferPos);
                var end = newline >= 0 ? newline : _bufferLen;
                _pending.Write(_buffer, _bufferPos, end - _bufferPos);
                _bufferPos = newline >= 0 ? newline + 1 : _bufferLen;

                if (_pending.Length > MaxLineLength)
                {
                    throw new IOException("Línea demasiado larga");
                }
                if (newline >= 0)
                {
                    return Encoding.UTF8.GetString(_pending.GetBuffer(), 0, (int)_pending.Length).TrimEnd('\r');
                }
            }
        }

        public async Task SendAsync(JsonObject message)
        {
            var bytes = Encoding.UTF8.GetBytes(message.ToJsonString() + "\n");
            await _writeLock.WaitAsync();
            try
            {
                await _stream.WriteAsync(bytes);
                await _stream.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Close()
        {
            try
            {
                _client.Close();
            }
            catch
            {
                // Ya cerrado.
            }
        }
    }
}
