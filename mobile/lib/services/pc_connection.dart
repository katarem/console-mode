import 'dart:async';

import 'package:flutter/foundation.dart';

import 'discovery.dart';
import 'models.dart';
import 'protocol.dart';
import 'storage.dart';

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

  SavedPc get pc => _pc;

  LineConnection? _conn;
  Timer? _pingTimer;
  Timer? _retryTimer;
  bool _active = false;
  bool _disposed = false;
  int _attempt = 0;

  void start() {
    if (_active) return;
    _active = true;
    _connect();
  }

  void stop() {
    _active = false;
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
      _goOffline('No se encuentra el PC en la red. ¿Está encendido y con Console Mode abierto?');
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
          if (supportsQuickSwitch) conn.send({'type': 'get_devices'});
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
          _notify();
        case 'status':
          mode = PcMode.fromWire(msg['mode'] as String?);
          busy = msg['busy'] == true;
          lastError = msg['error'] as String?;
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
    // Reintentos con espera creciente: 2, 4, 8, 15, 15... segundos.
    final delay = Duration(seconds: [2, 4, 8, 15][_attempt.clamp(0, 3)]);
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
