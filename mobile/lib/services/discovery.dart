import 'dart:async';
import 'dart:convert';
import 'dart:io';

/// PC encontrado por broadcast UDP en la red local.
class DiscoveredPc {
  DiscoveredPc({required this.pcId, required this.name, required this.host, required this.port});

  final String pcId;
  final String name;
  final String host;
  final int port;
}

/// Descubrimiento por broadcast UDP: el móvil envía "CONSOLEMODE_DISCOVER_V1" al puerto 47821
/// y cada PC con Console Mode responde con su id, nombre y puerto TCP.
class Discovery {
  static const discoveryPort = 47821;
  static const message = 'CONSOLEMODE_DISCOVER_V1';

  /// Emite cada PC encontrado (sin duplicados) durante [timeout].
  static Stream<DiscoveredPc> scan({Duration timeout = const Duration(seconds: 3)}) {
    late StreamController<DiscoveredPc> controller;
    RawDatagramSocket? socket;
    Timer? stopTimer;
    Timer? resendTimer;
    final seen = <String>{};

    Future<void> close() async {
      stopTimer?.cancel();
      resendTimer?.cancel();
      socket?.close();
      if (!controller.isClosed) await controller.close();
    }

    Future<void> start() async {
      try {
        socket = await RawDatagramSocket.bind(InternetAddress.anyIPv4, 0);
        socket!.broadcastEnabled = true;
        socket!.listen((event) {
          if (event != RawSocketEvent.read) return;
          final dg = socket?.receive();
          if (dg == null) return;
          try {
            final json = jsonDecode(utf8.decode(dg.data)) as Map<String, dynamic>;
            if (json['type'] != 'consolemode') return;
            final pcId = json['pcId'] as String;
            final host = dg.address.address;
            if (!seen.add('$pcId@$host')) return;
            controller.add(DiscoveredPc(
              pcId: pcId,
              name: (json['pcName'] as String?) ?? host,
              host: host,
              port: (json['port'] as num).toInt(),
            ));
          } catch (_) {
            // Respuesta de otra cosa: la ignoramos.
          }
        });

        final targets = await _broadcastAddresses();
        void send() {
          for (final target in targets) {
            try {
              socket?.send(utf8.encode(message), target, discoveryPort);
            } catch (_) {
              // Algunas interfaces no permiten broadcast.
            }
          }
        }

        send();
        // UDP puede perder paquetes: reenviamos un par de veces.
        resendTimer = Timer.periodic(const Duration(milliseconds: 800), (_) => send());
        stopTimer = Timer(timeout, close);
      } catch (e) {
        controller.addError(e);
        await close();
      }
    }

    controller = StreamController<DiscoveredPc>(onListen: start, onCancel: close);
    return controller.stream;
  }

  /// Busca un PC concreto (por id) para actualizar su IP si ha cambiado.
  static Future<DiscoveredPc?> find(String pcId, {Duration timeout = const Duration(seconds: 3)}) async {
    await for (final pc in scan(timeout: timeout)) {
      if (pc.pcId == pcId) return pc;
    }
    return null;
  }

  /// 255.255.255.255 más el broadcast de cada red local (suponiendo /24, lo habitual en casa).
  static Future<List<InternetAddress>> _broadcastAddresses() async {
    final result = <String>{'255.255.255.255'};
    try {
      final interfaces = await NetworkInterface.list(type: InternetAddressType.IPv4);
      for (final nic in interfaces) {
        for (final addr in nic.addresses) {
          final parts = addr.address.split('.');
          if (parts.length == 4 && !addr.isLoopback) {
            result.add('${parts[0]}.${parts[1]}.${parts[2]}.255');
          }
        }
      }
    } catch (_) {
      // Nos quedamos con el broadcast global.
    }
    return result.map(InternetAddress.new).toList();
  }
}
