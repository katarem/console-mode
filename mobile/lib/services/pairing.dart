import 'dart:async';

import 'models.dart';
import 'protocol.dart';
import 'storage.dart';

class PairingException implements Exception {
  PairingException(this.message);
  final String message;
  @override
  String toString() => message;
}

/// Empareja este móvil con un PC: el PC muestra un diálogo y, si el usuario acepta,
/// devuelve un token que se guarda para no volver a preguntar.
class Pairing {
  static Future<SavedPc> pair(
    PairTarget target,
    AppStorage storage, {
    void Function(String pcName)? onWaitingForApproval,
  }) async {
    final conn = await LineConnection.connectAny(target.hosts, target.port);
    try {
      final hello = await conn.next(timeout: const Duration(seconds: 5));
      if (hello == null || hello['type'] != 'hello') {
        throw PairingException('El equipo no ha respondido como Console Mode.');
      }
      final pcId = hello['pcId'] as String;
      final pcName = (hello['pcName'] as String?) ?? target.name ?? conn.host!;
      if (target.pcId != null && target.pcId != pcId) {
        throw PairingException('El código QR pertenece a otro PC.');
      }

      conn.send({
        'type': 'pair_request',
        'deviceId': storage.deviceId,
        'deviceName': storage.deviceName,
      });

      while (true) {
        final msg = await conn.next(timeout: const Duration(minutes: 2));
        if (msg == null) {
          throw PairingException('El PC ha cerrado la conexión.');
        }
        switch (msg['type']) {
          case 'pair_pending':
            onWaitingForApproval?.call(pcName);
          case 'pair_ok':
            // Ponemos primero la IP que ha funcionado.
            final hosts = [conn.host!, ...target.hosts.where((h) => h != conn.host)];
            final pc = SavedPc(
              id: pcId,
              name: (msg['pcName'] as String?) ?? pcName,
              hosts: hosts,
              port: target.port,
              token: msg['token'] as String,
            );
            await storage.upsertPc(pc);
            return pc;
          case 'pair_denied':
            throw PairingException(msg['reason'] == 'busy'
                ? 'El PC está atendiendo otra solicitud. Inténtalo de nuevo.'
                : 'La solicitud se ha rechazado en el PC.');
          case 'error':
            throw PairingException('${msg['message']}');
        }
      }
    } on TimeoutException {
      throw PairingException('Se ha agotado el tiempo de espera.');
    } finally {
      await conn.close();
    }
  }
}
