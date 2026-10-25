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
  FakePc({this.approve = true, this.bindPort = 0});

  static const wake = [
    {'mac': 'AA:BB:CC:DD:EE:FF', 'broadcast': '127.255.255.255'},
  ];

  final bool approve;
  final int bindPort;
  final pcId = 'pc-1';
  final tokens = <String, String>{};
  String mode = 'pc';
  String audio = 'tv';
  String mic = 'webcam';
  String comms = 'tv';
  String? power;
  int setModeCount = 0;
  late ServerSocket server;

  int get port => server.port;

  Future<void> start() async {
    server = await ServerSocket.bind(InternetAddress.loopbackIPv4, bindPort);
    server.listen(_handle);
  }

  void _handle(Socket s) {
    // Un móvil que corta la conexión de golpe no debe tumbar el test.
    s.done.catchError((Object _) => s);
    const nonce = 'n0nce';
    void send(Map<String, dynamic> m) => s.write('${jsonEncode(m)}\n');
    send({'type': 'hello', 'version': 5, 'pcId': pcId, 'pcName': 'FAKE-PC', 'nonce': nonce});
    void sendDevices() => send({
          'type': 'devices',
          'audio': [
            {'id': 'tv', 'name': 'SAMSUNG', 'current': audio == 'tv'},
            {'id': 'hp', 'name': 'Headphones', 'current': audio == 'hp'},
          ],
          'mics': [
            {'id': 'webcam', 'name': 'Webcam', 'current': mic == 'webcam'},
            {'id': 'headset', 'name': 'Arctis 7', 'current': mic == 'headset'},
          ],
          'comms': [
            {'id': 'tv', 'name': 'SAMSUNG', 'current': comms == 'tv'},
            {'id': 'hp', 'name': 'Headphones', 'current': comms == 'hp'},
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
          send({'type': 'pair_ok', 'token': tokens[msg['deviceId']], 'pcId': pcId, 'pcName': 'FAKE-PC', 'wake': wake});
        case 'auth':
          final token = tokens[msg['deviceId']];
          if (token == null || LineConnection.proof(token, nonce) != msg['proof']) {
            send({'type': 'auth_failed', 'reason': 'bad_proof'});
            return;
          }
          device = msg['deviceId'] as String;
          send({'type': 'auth_ok', 'pcName': 'FAKE-PC', 'wake': wake});
          send({'type': 'status', 'mode': mode, 'busy': false, 'error': null});
        case 'get_devices':
          if (device == null) return;
          sendDevices();
        case 'set_audio':
          if (device == null) return;
          audio = msg['id'] as String;
          sendDevices();
        case 'set_microphone':
          if (device == null) return;
          mic = msg['id'] as String;
          sendDevices();
        case 'power':
          if (device == null) return;
          power = msg['action'] as String;
          send({'type': 'power_ok', 'action': power});
        case 'set_communications':
          if (device == null) return;
          comms = msg['id'] as String;
          sendDevices();
        case 'set_mode':
          if (device == null) return;
          setModeCount++;
          send({'type': 'status', 'mode': mode, 'busy': true, 'error': null});
          mode = msg['mode'] as String;
          send({'type': 'status', 'mode': mode, 'busy': false, 'error': null});
      }
    }, onError: (_) {});
  }

  Future<void> stop() => server.close();
}

Future<void> waitFor(bool Function() condition, {Duration timeout = const Duration(seconds: 5)}) async {
  final deadline = DateTime.now().add(timeout);
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
    expect(storage.pcs.single.wake.single.mac, 'AA:BB:CC:DD:EE:FF');

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

    // Micrófono y comunicaciones (protocolo 4)
    expect(conn.microphones.firstWhere((o) => o.current).id, 'webcam');
    conn.setMicrophone('headset');
    await waitFor(() => conn.microphones.firstWhere((o) => o.current).id == 'headset');
    conn.setCommunications('hp');
    await waitFor(() => conn.communications.firstWhere((o) => o.current).id == 'hp');
    expect(pc.audio, 'hp');
    expect(pc.comms, 'hp');

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

  test('guarda los datos de encendido al conectar con un PC que ya estaba emparejado', () async {
    final pc = FakePc();
    await pc.start();
    final storage = await AppStorage.load();
    pc.tokens[storage.deviceId] = 'tok';
    final old = SavedPc(id: 'pc-1', name: 'FAKE-PC', hosts: ['127.0.0.1'], port: pc.port, token: 'tok');
    expect(old.canWake, isFalse);

    final conn = PcConnection(old, storage)..start();
    await waitFor(() => conn.status == ConnectionStatus.connected && conn.pc.canWake);
    expect(storage.pcs.single.wake.single.broadcast, '127.255.255.255');
    await waitFor(() => conn.audio.length == 2); // que el PC falso termine de responder antes de cerrar
    conn.dispose();
    await pc.stop();
  });

  test('enciende el PC y entra en modo consola cuando responde', () async {
    // Puerto libre en el que todavía no escucha nadie: el PC está «apagado».
    final probe = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    final port = probe.port;
    await probe.close();

    final storage = await AppStorage.load();
    final saved = SavedPc(
      id: 'pc-1',
      name: 'FAKE-PC',
      hosts: ['127.0.0.1'],
      port: port,
      token: 'tok',
      wake: WakeTarget.listFromJson(FakePc.wake),
    );
    final conn = PcConnection(saved, storage)..start();
    await waitFor(() => conn.status == ConnectionStatus.offline, timeout: const Duration(seconds: 15));

    await conn.wake(thenMode: PcMode.console);
    expect(conn.waking, isTrue);

    final pc = FakePc(bindPort: port);
    await pc.start();
    pc.tokens[storage.deviceId] = 'tok';

    await waitFor(() => conn.mode == PcMode.console && !conn.busy, timeout: const Duration(seconds: 20));
    expect(conn.waking, isFalse);
    expect(pc.mode, 'console');
    await waitFor(() => conn.audio.length == 2);
    conn.dispose();
    await pc.stop();
  });

  test('suspende o apaga el PC según lo elegido y lo guarda', () async {
    final pc = FakePc();
    await pc.start();
    final storage = await AppStorage.load();
    pc.tokens[storage.deviceId] = 'tok';
    final saved = SavedPc(id: 'pc-1', name: 'FAKE-PC', hosts: ['127.0.0.1'], port: pc.port, token: 'tok');
    await storage.upsertPc(saved);
    expect(saved.powerOff, PowerOffAction.sleep);
    expect(saved.wakeMode, WakeMode.console);

    final conn = PcConnection(saved, storage)..start();
    await waitFor(() => conn.status == ConnectionStatus.connected && conn.supportsPower);
    await waitFor(() => conn.audio.length == 2);

    await conn.updatePowerSettings(powerOff: PowerOffAction.shutdown, wakeMode: WakeMode.pc);
    expect(storage.pcs.single.powerOff, PowerOffAction.shutdown);
    expect(storage.pcs.single.wakeMode, WakeMode.pc);

    conn.powerOff();
    expect(conn.poweringOff, isTrue);
    await waitFor(() => pc.power == 'shutdown' && conn.status == ConnectionStatus.offline);
    expect(conn.lastError, isNull);
    conn.dispose();
    await pc.stop();
  });

  test('al encender usa el modo elegido para ese PC', () async {
    final probe = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    final port = probe.port;
    await probe.close();

    final storage = await AppStorage.load();
    final saved = SavedPc(
      id: 'pc-1',
      name: 'FAKE-PC',
      hosts: ['127.0.0.1'],
      port: port,
      token: 'tok',
      wake: WakeTarget.listFromJson(FakePc.wake),
      wakeMode: WakeMode.pc,
    );
    final conn = PcConnection(saved, storage)..start();
    await waitFor(() => conn.status == ConnectionStatus.offline, timeout: const Duration(seconds: 15));

    final pc = FakePc(bindPort: port)..mode = 'console';
    await pc.start();
    pc.tokens[storage.deviceId] = 'tok';
    await conn.wake();

    await waitFor(() => conn.mode == PcMode.pc && !conn.busy, timeout: const Duration(seconds: 20));
    expect(pc.mode, 'pc');
    await waitFor(() => conn.audio.length == 2);
    conn.dispose();
    await pc.stop();
  });

  test('los PCs guardados sin ajustes de encendido usan los de por defecto', () {
    final pc = SavedPc.fromJson({'id': 'a', 'name': 'PC', 'hosts': ['1.2.3.4'], 'port': 1, 'token': 't'});
    expect(pc.wakeMode, WakeMode.console);
    expect(pc.powerOff, PowerOffAction.sleep);
    final roundTrip = SavedPc.fromJson(pc.copyWith(wakeMode: WakeMode.keep, powerOff: PowerOffAction.shutdown).toJson());
    expect(roundTrip.wakeMode, WakeMode.keep);
    expect(roundTrip.powerOff, PowerOffAction.shutdown);
  });

  test('al encender pide el modo aunque el PC diga que ya está en él', () async {
    final probe = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    final port = probe.port;
    await probe.close();

    final storage = await AppStorage.load();
    final saved = SavedPc(
      id: 'pc-1',
      name: 'FAKE-PC',
      hosts: ['127.0.0.1'],
      port: port,
      token: 'tok',
      wake: WakeTarget.listFromJson(FakePc.wake),
    );
    final conn = PcConnection(saved, storage)..start();
    await waitFor(() => conn.status == ConnectionStatus.offline, timeout: const Duration(seconds: 15));
    await conn.wake();

    // Se apagó en modo consola: al arrancar Console Mode sigue diciendo «consola», pero el frontal no está abierto.
    final pc = FakePc(bindPort: port)..mode = 'console';
    pc.tokens[storage.deviceId] = 'tok';
    await pc.start();

    await waitFor(() => pc.setModeCount == 1 && !conn.busy, timeout: const Duration(seconds: 20));
    expect(pc.mode, 'console');
    expect(storage.wakeRequest('pc-1'), isNull);
    await waitFor(() => conn.audio.length == 2);
    conn.dispose();
    await pc.stop();
  });

  test('la petición de encendido sobrevive a cerrar la app mientras el PC arranca', () async {
    final probe = await ServerSocket.bind(InternetAddress.loopbackIPv4, 0);
    final port = probe.port;
    await probe.close();

    final storage = await AppStorage.load();
    final saved = SavedPc(
      id: 'pc-1',
      name: 'FAKE-PC',
      hosts: ['127.0.0.1'],
      port: port,
      token: 'tok',
      wake: WakeTarget.listFromJson(FakePc.wake),
    );
    final first = PcConnection(saved, storage)..start();
    await waitFor(() => first.status == ConnectionStatus.offline, timeout: const Duration(seconds: 15));
    await first.wake();
    expect(storage.wakeRequest('pc-1')?.mode, PcMode.console);
    first.dispose(); // la app se cierra

    final pc = FakePc(bindPort: port);
    pc.tokens[storage.deviceId] = 'tok';
    await pc.start();

    final second = PcConnection(saved, storage)..start(); // se vuelve a abrir
    expect(second.waking, isTrue);
    await waitFor(() => pc.mode == 'console' && second.mode == PcMode.console && !second.busy,
        timeout: const Duration(seconds: 20));
    await waitFor(() => second.audio.length == 2);
    second.dispose();
    await pc.stop();
  });
}
