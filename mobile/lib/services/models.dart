/// PC guardado en el móvil después de emparejarlo.
class SavedPc {
  SavedPc({
    required this.id,
    required this.name,
    required this.hosts,
    required this.port,
    required this.token,
  });

  final String id;
  final String name;
  final List<String> hosts;
  final int port;
  final String token;

  SavedPc copyWith({String? name, List<String>? hosts, int? port, String? token}) => SavedPc(
        id: id,
        name: name ?? this.name,
        hosts: hosts ?? this.hosts,
        port: port ?? this.port,
        token: token ?? this.token,
      );

  Map<String, dynamic> toJson() => {
        'id': id,
        'name': name,
        'hosts': hosts,
        'port': port,
        'token': token,
      };

  factory SavedPc.fromJson(Map<String, dynamic> json) => SavedPc(
        id: json['id'] as String,
        name: json['name'] as String,
        hosts: (json['hosts'] as List).cast<String>(),
        port: json['port'] as int,
        token: json['token'] as String,
      );
}

/// PC al que queremos emparejarnos (leído del QR o encontrado en la red).
class PairTarget {
  PairTarget({required this.hosts, required this.port, this.pcId, this.name});

  final List<String> hosts;
  final int port;
  final String? pcId;
  final String? name;

  static const scheme = 'consolemode';

  /// Interpreta `consolemode://pair?h=ip1,ip2&p=47820&id=...&n=NOMBRE`.
  static PairTarget? tryParseUri(String raw) {
    final uri = Uri.tryParse(raw.trim());
    if (uri == null || uri.scheme != scheme || uri.host != 'pair') {
      return null;
    }
    final hosts = (uri.queryParameters['h'] ?? '')
        .split(',')
        .map((h) => h.trim())
        .where((h) => h.isNotEmpty)
        .toList();
    final port = int.tryParse(uri.queryParameters['p'] ?? '');
    if (hosts.isEmpty || port == null) {
      return null;
    }
    return PairTarget(
      hosts: hosts,
      port: port,
      pcId: uri.queryParameters['id'],
      name: uri.queryParameters['n'],
    );
  }
}

/// Salida de audio o pantalla que se puede elegir al momento (cambio temporal, no se guarda en los modos).
class QuickOption {
  const QuickOption({required this.id, required this.name, required this.current});

  final String id;
  final String name;
  final bool current;

  static List<QuickOption> listFromJson(Object? json) => [
        for (final item in (json as List? ?? const []).cast<Map<String, dynamic>>())
          QuickOption(
            id: item['id'] as String,
            name: item['name'] as String,
            current: item['current'] == true,
          ),
      ];
}

enum PcMode {
  pc('pc', 'PC'),
  console('console', 'Consola');

  const PcMode(this.wire, this.label);

  final String wire;
  final String label;

  static PcMode? fromWire(String? value) {
    for (final m in values) {
      if (m.wire == value) return m;
    }
    return null;
  }
}
