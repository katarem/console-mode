import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import 'discovery.dart';
import 'models.dart';

/// Enciende o despierta un PC con Wake-on-LAN: el «paquete mágico» son 6 bytes 0xFF seguidos de la MAC repetida
/// 16 veces, enviado por broadcast UDP a la red local.
class WakeOnLan {
  static const ports = [9, 7];

  /// Paquete mágico para una MAC `AA:BB:CC:DD:EE:FF` (también admite `-` o sin separadores).
  static Uint8List magicPacket(String mac) {
    final hex = mac.replaceAll(RegExp(r'[^0-9a-fA-F]'), '');
    if (hex.length != 12) {
      throw FormatException('MAC no válida: $mac');
    }
    final bytes = [for (var i = 0; i < 12; i += 2) int.parse(hex.substring(i, i + 2), radix: 16)];
    return Uint8List.fromList([...List.filled(6, 0xFF), for (var i = 0; i < 16; i++) ...bytes]);
  }

  /// Envía el paquete mágico de cada tarjeta del PC varias veces (UDP puede perder paquetes).
  static Future<void> wake(List<WakeTarget> targets) async {
    if (targets.isEmpty) return;
    final socket = await RawDatagramSocket.bind(InternetAddress.anyIPv4, 0);
    try {
      socket.broadcastEnabled = true;
      final addresses = <String>{
        for (final t in targets) t.broadcast,
        for (final a in await Discovery.broadcastAddresses()) a.address,
      }.map(InternetAddress.tryParse).whereType<InternetAddress>().toList();

      for (var round = 0; round < 3; round++) {
        for (final target in targets) {
          final packet = magicPacket(target.mac);
          for (final address in addresses) {
            for (final port in ports) {
              try {
                socket.send(packet, address, port);
              } catch (_) {
                // Algunas interfaces no permiten broadcast.
              }
            }
          }
        }
        await Future<void>.delayed(const Duration(milliseconds: 300));
      }
    } finally {
      socket.close();
    }
  }
}
