import 'package:console_mode/services/models.dart';
import 'package:console_mode/services/protocol.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('PairTarget.tryParseUri', () {
    test('lee hosts, puerto, id y nombre', () {
      final t = PairTarget.tryParseUri(
          'consolemode://pair?h=192.168.1.10%2C10.0.0.5&p=47820&id=abc123&n=MI-PC');
      expect(t, isNotNull);
      expect(t!.hosts, ['192.168.1.10', '10.0.0.5']);
      expect(t.port, 47820);
      expect(t.pcId, 'abc123');
      expect(t.name, 'MI-PC');
    });

    test('rechaza otros QR', () {
      expect(PairTarget.tryParseUri('https://example.com'), isNull);
      expect(PairTarget.tryParseUri('consolemode://pair?p=47820'), isNull);
      expect(PairTarget.tryParseUri('consolemode://pair?h=1.2.3.4'), isNull);
    });
  });

  test('la prueba HMAC coincide con la del PC (HMAC-SHA256 en hex)', () {
    expect(
      LineConnection.proof('token-123', 'nonce-abc'),
      '2059d87586c6ef90d550b753f65bd775597a1b10525eb81090d7a5aaef0056bf',
    );
  });
}
