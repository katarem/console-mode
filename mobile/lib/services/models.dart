/// Tarjeta de red del PC a la que se envía el paquete mágico de Wake-on-LAN.
class WakeTarget {
  const WakeTarget({required this.mac, required this.broadcast});

  /// MAC en formato `AA:BB:CC:DD:EE:FF`.
  final String mac;

  /// Dirección de broadcast de su red, p. ej. `192.168.1.255`.
  final String broadcast;

  Map<String, dynamic> toJson() => {'mac': mac, 'broadcast': broadcast};

  static List<WakeTarget> listFromJson(Object? json) => [
        for (final item in (json as List? ?? const []).whereType<Map>())
          if (item['mac'] is String && item['broadcast'] is String)
            WakeTarget(mac: item['mac'] as String, broadcast: item['broadcast'] as String),
      ];

  @override
  bool operator ==(Object other) => other is WakeTarget && other.mac == mac && other.broadcast == broadcast;

  @override
  int get hashCode => Object.hash(mac, broadcast);
}

/// PC guardado en el móvil después de emparejarlo.
class SavedPc {
  SavedPc({
    required this.id,
    required this.name,
    required this.hosts,
    required this.port,
    required this.token,
    this.wake = const [],
    this.wakeMode = WakeMode.console,
    this.powerOff = PowerOffAction.sleep,
  });

  final String id;
  final String name;
  final List<String> hosts;
  final int port;
  final String token;

  /// Datos para encenderlo con Wake-on-LAN (vacío si el PC tiene una versión sin esta función).
  final List<WakeTarget> wake;

  /// Modo en el que se pone el PC al encenderlo desde el móvil.
  final WakeMode wakeMode;

  /// Qué hace el botón de apagar.
  final PowerOffAction powerOff;

  bool get canWake => wake.isNotEmpty;

  SavedPc copyWith({
    String? name,
    List<String>? hosts,
    int? port,
    String? token,
    List<WakeTarget>? wake,
    WakeMode? wakeMode,
    PowerOffAction? powerOff,
  }) =>
      SavedPc(
        id: id,
        name: name ?? this.name,
        hosts: hosts ?? this.hosts,
        port: port ?? this.port,
        token: token ?? this.token,
        wake: wake ?? this.wake,
        wakeMode: wakeMode ?? this.wakeMode,
        powerOff: powerOff ?? this.powerOff,
      );

  Map<String, dynamic> toJson() => {
        'id': id,
        'name': name,
        'hosts': hosts,
        'port': port,
        'token': token,
        'wake': wake.map((w) => w.toJson()).toList(),
        'wakeMode': wakeMode.wire,
        'powerOff': powerOff.wire,
      };

  factory SavedPc.fromJson(Map<String, dynamic> json) => SavedPc(
        id: json['id'] as String,
        name: json['name'] as String,
        hosts: (json['hosts'] as List).cast<String>(),
        port: json['port'] as int,
        token: json['token'] as String,
        wake: WakeTarget.listFromJson(json['wake']),
        wakeMode: WakeMode.fromWire(json['wakeMode'] as String?),
        powerOff: PowerOffAction.fromWire(json['powerOff'] as String?),
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

/// Modo en el que se pone el PC después de encenderlo desde el móvil.
enum WakeMode {
  console('console', 'Consola', PcMode.console),
  pc('pc', 'PC', PcMode.pc),
  keep('keep', 'Sin cambiar', null);

  const WakeMode(this.wire, this.label, this.mode);

  final String wire;
  final String label;

  /// Modo que se pide al PC al conectar, o null para dejarlo como esté.
  final PcMode? mode;

  static WakeMode fromWire(String? value) => values.firstWhere((m) => m.wire == value, orElse: () => WakeMode.console);
}

/// Qué hace el botón de apagar: suspender (más rápido y el Wake-on-LAN es más fiable) o apagar del todo.
enum PowerOffAction {
  sleep('sleep', 'Suspender', 'Suspendiendo'),
  shutdown('shutdown', 'Apagar', 'Apagando');

  const PowerOffAction(this.wire, this.label, this.progress);

  final String wire;
  final String label;
  final String progress;

  static PowerOffAction fromWire(String? value) =>
      values.firstWhere((a) => a.wire == value, orElse: () => PowerOffAction.sleep);
}
