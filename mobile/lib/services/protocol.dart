import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:crypto/crypto.dart';

/// Conexión de bajo nivel: un objeto JSON por línea sobre TCP (ver docs/PROTOCOL.md).
class LineConnection {
  LineConnection._(this._socket)
      : _messages = StreamIterator(_socket
            .cast<List<int>>()
            .transform(utf8.decoder)
            .transform(const LineSplitter())
            .map((line) => jsonDecode(line) as Map<String, dynamic>));

  final Socket _socket;
  final StreamIterator<Map<String, dynamic>> _messages;
  String? host;

  /// Prueba las direcciones en orden y devuelve la primera que conecta.
  static Future<LineConnection> connectAny(List<String> hosts, int port,
      {Duration timeout = const Duration(seconds: 4)}) async {
    Object? lastError;
    for (final host in hosts) {
      try {
        final socket = await Socket.connect(host, port, timeout: timeout);
        socket.setOption(SocketOption.tcpNoDelay, true);
        return LineConnection._(socket)..host = host;
      } catch (e) {
        lastError = e;
      }
    }
    throw SocketException('No se pudo conectar con el PC (${hosts.join(', ')}:$port): $lastError');
  }

  /// Siguiente mensaje, o null si el PC ha cerrado la conexión.
  Future<Map<String, dynamic>?> next({Duration? timeout}) async {
    final moved = timeout == null ? _messages.moveNext() : _messages.moveNext().timeout(timeout);
    return await moved ? _messages.current : null;
  }

  void send(Map<String, dynamic> message) {
    _socket.write('${jsonEncode(message)}\n');
  }

  Future<void> close() async {
    try {
      await _messages.cancel();
    } catch (_) {}
    _socket.destroy();
  }

  /// HMAC-SHA256(clave = token, mensaje = nonce) en hexadecimal.
  static String proof(String token, String nonce) =>
      Hmac(sha256, utf8.encode(token)).convert(utf8.encode(nonce)).toString();
}
