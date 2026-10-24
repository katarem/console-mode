import 'dart:async';

import 'package:flutter/foundation.dart';

import 'discovery.dart';
import 'models.dart';
import 'protocol.dart';
import 'storage.dart';
import 'wake_on_lan.dart';

enum ConnectionStatus { connecting, connected, offline, unauthorized }

/// Conexión persistente con un PC emparejado: se autentica, recibe el modo actual
/// y permite cambiarlo. Se reconecta sola mientras está activa.
class PcConnection extends ChangeNotifier {
  PcConnection(this._pc, this._storage);

  SavedPc _pc;
  final AppStorage _storage;

  ConnectionStatus status = ConnectionStatus.connecting;
  PcMode? mode;
  bool busy = false;
  String? lastError;

  /// El PC permite cambiar audio y pantalla al momento (protocolo 2 o superior).
  bool supportsQuickSwitch = false;
  List<QuickOption> audio = const [];
  List<QuickOption> displays = const [];

  /// Micrófonos y salidas de comunicaciones (Discord, Teams...). Vacías si el PC usa un protocolo anterior al 4.
  List<QuickOption> microphones = const [];
  List<QuickOption> communications = const [];

  /// Se ha enviado la señal de encendido y se espera a que el PC arranque.
  bool waking = false;

  SavedPc get pc => _pc;

  /// Tiempo máximo de espera tras enviar la señal de encendido (arrancar Windows puede tardar).
  static const wakeTimeout = Duration(minutes: 3);

  LineConnection? _conn;
  Timer? _pingTimer;
  Timer? _retryTimer;
  bool _active = false;
  bool _disposed = false;
  int _attempt = 0;
  PcMode? _modeAfterWake;
  PcMode? _pendingMode;
  Timer? _wakeTimer;
  Timer? _wakeResendTimer;

  void start() {
    if (_active) return;
    _active = true;
    _connect();
  }

  void stop() {
    _active = false;
    _endWake();
    _retryTimer?.cancel();
    _closeConnection();
  }

  /// Reintenta ya (p.ej. al pulsar "Reintentar" o al volver a la app).
  void retryNow() {
    if (status == ConnectionStatus.connected || status == ConnectionStatus.connecting) return;
    _active = true;
    _retryTimer?.cancel();
    _attempt = 0;
    _connect();
  }

  /// Enciende o despierta el PC con Wake-on-LAN y espera a que vuelva. Con [thenMode], al conectar cambia a ese modo.
  Future<void> wake({PcMode? thenMode}) async {
    if (!_pc.canWake || status == ConnectionStatus.connected) return;
    _modeAfterWake = thenMode;
    waking = true;
    lastError = null;
    _notify();
    try {
      await WakeOnLan.wake(_pc.wake);
    } catch (e) {
      _endWake(error: 'No se pudo enviar la señal de encendido: $e');
      return;
    }
    // Se repite la señal de vez en cuando por si se pierde y se reintenta conectar a menudo mientras arranca.
    _wakeResendTimer?.cancel();
    _wakeResendTimer = Timer.periodic(const Duration(seconds: 15), (_) => WakeOnLan.wake(_pc.wake).ignore());
    _wakeTimer?.cancel();
    _wakeTimer = Timer(wakeTimeout, () {
      if (status != ConnectionStatus.connected) {
        _endWake(
            error: 'El PC no ha respondido. Comprueba que Wake-on-LAN esté activado en la BIOS y en la pestaña '
                '«Encendido» de Console Mode, y que el PC esté conectado por cable.');
      }
    });
    _active = true;
    _attempt = 0;
    if (status != ConnectionStatus.connecting) {
      _retryTimer?.cancel();
      _connect();
    }
  }

  void _endWake({String? error}) {
    _wakeTimer?.cancel();
    _wakeResendTimer?.cancel();
    if (!waking) return;
    waking = false;
    _modeAfterWake = null;
    if (error != null) lastError = error;
    _notify();
  }

  void setMode(PcMode target) {
    final conn = _conn;
    if (conn == null || status != ConnectionStatus.connected || busy) return;
    lastError = null;
    busy = true;
    _notify();
    conn.send({'type': 'set_mode', 'mode': target.wire});
  }

  /// Cambia la salida de audio del PC sin tocar la configuración de sus modos.
  void setAudio(String id) => _sendQuick('set_audio', id);

  /// Cambia la pantalla principal del PC sin tocar la configuración de sus modos.
  void setDisplay(String id) => _sendQuick('set_display', id);

  /// Cambia el micrófono del PC (predeterminado y de comunicaciones) sin tocar sus modos.
  void setMicrophone(String id) => _sendQuick('set_microphone', id);

  /// Cambia la salida de comunicaciones del PC (la que usan Discord, Teams...) sin tocar sus modos.
  void setCommunications(String id) => _sendQuick('set_communications', id);

  void _sendQuick(String type, String id) {
    final conn = _conn;
    if (conn == null || status != ConnectionStatus.connected || !supportsQuickSwitch) return;
    lastError = null;
    conn.send({'type': type, 'id': id});
  }

  Future<void> _connect() async {
    if (!_active) return;
    status = ConnectionStatus.connecting;
    _notify();

    LineConnection? conn;
    try {
      conn = await LineConnection.connectAny(_pc.hosts, _pc.port);
    } catch (_) {
      // ¿Ha cambiado la IP del PC? Lo buscamos en la red por su id.
      final found = await Discovery.find(_pc.id);
      if (found != null) {
        _pc = _pc.copyWith(hosts: [found.host, ..._pc.hosts.where((h) => h != found.host)], port: found.port);
        await _storage.upsertPc(_pc);
        try {
          conn = await LineConnection.connectAny(_pc.hosts, _pc.port);
        } catch (_) {}
      }
    }

    if (!_active) {
      await conn?.close();
      return;
    }
    if (conn == null) {
      _goOffline(waking ? null : 'No se encuentra el PC en la red. ¿Está encendido y con Console Mode abierto?');
      return;
    }

    _conn = conn;
    try {
      await _session(conn);
    } catch (e) {
      if (status != ConnectionStatus.unauthorized) {
        lastError = e is TimeoutException ? 'El PC no responde.' : null;
      }
    } finally {
      _closeConnection();
      if (status != ConnectionStatus.unauthorized) {
        _goOffline(lastError);
      }
    }
  }

  Future<void> _session(LineConnection conn) async {
    final hello = await conn.next(timeout: const Duration(seconds: 5));
    if (hello == null || hello['type'] != 'hello') return;
    supportsQuickSwitch = ((hello['version'] as num?) ?? 1) >= 2;
    if (hello['pcId'] != _pc.id) {
      lastError = 'En esa dirección hay otro PC.';
      return;
    }

    conn.send({
      'type': 'auth',
      'deviceId': _storage.deviceId,
      'deviceName': _storage.deviceName,
      'proof': LineConnection.proof(_pc.token, hello['nonce'] as String),
    });

    while (true) {
      // El PC responde a nuestros ping cada 15 s; si no llega nada en 40 s damos la conexión por perdida.
      final msg = await conn.next(timeout: const Duration(seconds: 40));
      if (msg == null) return;
      switch (msg['type']) {
        case 'auth_ok':
          status = ConnectionStatus.connected;
          lastError = null;
          _attempt = 0;
          _pingTimer?.cancel();
          _pingTimer = Timer.periodic(const Duration(seconds: 15), (_) => conn.send({'type': 'ping'}));
          final name = msg['pcName'] as String?;
          if (name != null && name != _pc.name) {
            _pc = _pc.copyWith(name: name);
            await _storage.upsertPc(_pc);
          }
          final wake = WakeTarget.listFromJson(msg['wake']);
          if (wake.isNotEmpty && !listEquals(wake, _pc.wake)) {
            _pc = _pc.copyWith(wake: wake);
            await _storage.upsertPc(_pc);
          }
          if (supportsQuickSwitch) conn.send({'type': 'get_devices'});
          // Si se ha encendido para entrar en un modo, se pide al llegar el estado (justo después de auth_ok).
          _pendingMode = _modeAfterWake;
          _endWake();
          _notify();
        case 'auth_failed':
          status = ConnectionStatus.unauthorized;
          _active = false;
          lastError = 'Este móvil ya no es de confianza en el PC. Vuelve a emparejarlo.';
          _notify();
          return;
        case 'devices':
          audio = QuickOption.listFromJson(msg['audio']);
          displays = QuickOption.listFromJson(msg['displays']);
          microphones = QuickOption.listFromJson(msg['mics']);
          communications = QuickOption.listFromJson(msg['comms']);
          _notify();
        case 'status':
          mode = PcMode.fromWire(msg['mode'] as String?);
          busy = msg['busy'] == true;
          lastError = msg['error'] as String?;
          final pending = _pendingMode;
          _pendingMode = null;
          if (pending != null && !busy && mode != pending) {
            busy = true;
            conn.send({'type': 'set_mode', 'mode': pending.wire});
          }
          _notify();
        case 'error':
          busy = false;
          lastError = msg['message'] as String?;
          _notify();
      }
    }
  }

  void _goOffline(String? error) {
    status = ConnectionStatus.offline;
    busy = false;
    lastError = error;
    _notify();
    if (!_active) return;
    // Reintentos con espera creciente: 2, 4, 8, 15, 15... segundos (cada 3 s mientras el PC se enciende).
    final delay = waking ? const Duration(seconds: 3) : Duration(seconds: [2, 4, 8, 15][_attempt.clamp(0, 3)]);
    _attempt++;
    _retryTimer?.cancel();
    _retryTimer = Timer(delay, _connect);
  }

  void _closeConnection() {
    _pingTimer?.cancel();
    _pingTimer = null;
    final conn = _conn;
    _conn = null;
    conn?.close();
  }

  void _notify() {
    if (!_disposed) notifyListeners();
  }

  @override
  void dispose() {
    _disposed = true;
    stop();
    super.dispose();
  }
}
