import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:console_mode/services/models.dart';
import 'package:console_mode/services/pairing.dart';
import 'package:console_mode/services/pc_connection.dart';
import 'package:console_mode/services/protocol.dart';
import 'package:console_mode/services/storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Servidor falso que imita a la app de PC.
class FakePc {
  FakePc({this.approve = true});

  final bool approve;
  final pcId = 'pc-1';
  final tokens = <String, String>{};
  String mode = 'pc';
  String audio = 'tv';
  late ServerSocket server;

  int get port => server.port;

  Future<void> start() async {
    server = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    server.listen(_handle);
  }

  void _handle(Socket s) {
    const nonce = 'n0nce';
    void send(Map<String, dynamic> m) => s.write('${jsonEncode(m)}\n');
    send({'type': 'hello', 'version': 2, 'pcId': pcId, 'pcName': 'FAKE-PC', 'nonce': nonce});
    void sendDevices() => send({
          'type': 'devices',
          'audio': [
            {'id': 'tv', 'name': 'SAMSUNG', 'current': audio == 'tv'},
            {'id': 'hp', 'name': 'Headphones', 'current': audio == 'hp'},
          ],
          'displays': [
            {'id': 'd1', 'name': 'SAMSUNG', 'current': true},
          ],
        });
    String? device;
    s.cast<List<int>>().transform(utf8.decoder).transform(const LineSplitter()).listen((line) async {
      final msg = jsonDecode(line) as Map<String, dynamic>;
      switch (msg['type']) {
        case 'pair_request':
          send({'type': 'pair_pending'});
          await Future<void>.delayed(const Duration(milliseconds: 50));
          if (!approve) {
            send({'type': 'pair_denied', 'reason': 'rejected'});
            return;
          }
          tokens[msg['deviceId'] as String] = 'tok-${msg['deviceId']}';
          send({'type': 'pair_ok', 'token': tokens[msg['deviceId']], 'pcId': pcId, 'pcName': 'FAKE-PC'});
        case 'auth':
          final token = tokens[msg['deviceId']];
          if (token == null || LineConnection.proof(token, nonce) != msg['proof']) {
            send({'type': 'auth_failed', 'reason': 'bad_proof'});
            return;
          }
          device = msg['deviceId'] as String;
          send({'type': 'auth_ok', 'pcName': 'FAKE-PC'});
          send({'type': 'status', 'mode': mode, 'busy': false, 'error': null});
        case 'get_devices':
          if (device == null) return;
          sendDevices();
        case 'set_audio':
          if (device == null) return;
          audio = msg['id'] as String;
          sendDevices();
        case 'set_mode':
          if (device == null) return;
          send({'type': 'status', 'mode': mode, 'busy': true, 'error': null});
          mode = msg['mode'] as String;
          send({'type': 'status', 'mode': mode, 'busy': false, 'error': null});
      }
    }, onError: (_) {});
  }

  Future<void> stop() => server.close();
}

Future<void> waitFor(bool Function() condition) async {
  final deadline = DateTime.now().add(const Duration(seconds: 5));
  while (!condition()) {
    if (DateTime.now().isAfter(deadline)) throw TimeoutException('condición no cumplida');
    await Future<void>.delayed(const Duration(milliseconds: 20));
  }
}

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  setUp(() => SharedPreferences.setMockInitialValues({}));

  test('empareja, se autentica y cambia de modo', () async {
    final pc = FakePc();
    await pc.start();
    final storage = await AppStorage.load();

    var waited = false;
    final saved = await Pairing.pair(
      PairTarget(hosts: ['127.0.0.1'], port: pc.port),
      storage,
      onWaitingForApproval: (_) => waited = true,
    );
    expect(waited, isTrue);
    expect(saved.id, 'pc-1');
    expect(saved.name, 'FAKE-PC');
    expect(storage.pcs.single.token, 'tok-${storage.deviceId}');

    final conn = PcConnection(saved, storage)..start();
    await waitFor(() => conn.status == ConnectionStatus.connected && conn.mode == PcMode.pc);

    conn.setMode(PcMode.console);
    await waitFor(() => conn.mode == PcMode.console && !conn.busy);

    // Cambio rápido de audio (protocolo 2)
    await waitFor(() => conn.audio.length == 2);
    expect(conn.supportsQuickSwitch, isTrue);
    expect(conn.audio.firstWhere((o) => o.current).id, 'tv');
    conn.setAudio('hp');
    await waitFor(() => conn.audio.firstWhere((o) => o.current).id == 'hp');

    conn.dispose();
    await pc.stop();
  });

  test('si el PC rechaza, falla el emparejamiento', () async {
    final pc = FakePc(approve: false);
    await pc.start();
    final storage = await AppStorage.load();

    await expectLater(
      Pairing.pair(PairTarget(hosts: ['127.0.0.1'], port: pc.port), storage),
      throwsA(isA<PairingException>()),
    );
    expect(storage.pcs, isEmpty);
    await pc.stop();
  });

  test('un token incorrecto deja la conexión como no autorizada', () async {
    final pc = FakePc();
    await pc.start();
    final storage = await AppStorage.load();
    final fake = SavedPc(id: 'pc-1', name: 'FAKE-PC', hosts: ['127.0.0.1'], port: pc.port, token: 'malo');

    final conn = PcConnection(fake, storage)..start();
    await waitFor(() => conn.status == ConnectionStatus.unauthorized);
    conn.dispose();
    await pc.stop();
  });
}
