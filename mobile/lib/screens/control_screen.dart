import 'package:flutter/material.dart';

import '../services/models.dart';
import '../services/pc_connection.dart';
import '../services/storage.dart';
import '../widgets/mode_toggle.dart';

/// Pantalla principal de control de un PC: interruptor PC / Consola.
class ControlScreen extends StatefulWidget {
  const ControlScreen({super.key, required this.pc, required this.storage});

  final SavedPc pc;
  final AppStorage storage;

  @override
  State<ControlScreen> createState() => _ControlScreenState();
}

class _ControlScreenState extends State<ControlScreen> with WidgetsBindingObserver {
  late final PcConnection _connection = PcConnection(widget.pc, widget.storage)..start();

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Android/iOS cortan los sockets en segundo plano: reconectamos al volver.
    if (state == AppLifecycleState.resumed) {
      _connection.retryNow();
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _connection.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return ListenableBuilder(
      listenable: _connection,
      builder: (context, _) {
        final c = _connection;
        final connected = c.status == ConnectionStatus.connected;
        return Scaffold(
          appBar: AppBar(title: Text(c.pc.name)),
          body: SafeArea(
            child: LayoutBuilder(
              builder: (context, constraints) => SingleChildScrollView(
                padding: const EdgeInsets.all(24),
                child: ConstrainedBox(
                  constraints: BoxConstraints(minHeight: constraints.maxHeight - 48),
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      _StatusBanner(connection: c),
                      const SizedBox(height: 24),
                      ConstrainedBox(
                        constraints: const BoxConstraints(maxWidth: 360),
                        child: ModeToggle(
                          mode: c.mode,
                          enabled: connected,
                          busy: c.busy,
                          onChanged: c.setMode,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        );
      },
    );
  }
}

class _StatusBanner extends StatelessWidget {
  const _StatusBanner({required this.connection});

  final PcConnection connection;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final c = connection;

    final (IconData icon, String text, Color color) = switch (c.status) {
      ConnectionStatus.connecting => (Icons.sync, 'Conectando…', scheme.tertiary),
      ConnectionStatus.connected => (
          Icons.check_circle,
          c.busy ? 'Cambiando de modo…' : 'Conectado · modo ${c.mode?.label ?? '?'}',
          Colors.green,
        ),
      ConnectionStatus.offline => (Icons.cloud_off, 'Sin conexión', scheme.error),
      ConnectionStatus.unauthorized => (Icons.lock_outline, 'Acceso revocado', scheme.error),
    };

    return Column(
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Icon(icon, color: color, size: 20),
            const SizedBox(width: 8),
            Text(text, style: Theme.of(context).textTheme.titleMedium),
          ],
        ),
        if (c.lastError != null) ...[
          const SizedBox(height: 12),
          Text(c.lastError!, textAlign: TextAlign.center, style: TextStyle(color: scheme.error)),
        ],
        if (c.status == ConnectionStatus.offline) ...[
          const SizedBox(height: 12),
          OutlinedButton.icon(
            onPressed: c.retryNow,
            icon: const Icon(Icons.refresh),
            label: const Text('Reintentar ahora'),
          ),
        ],
      ],
    );
  }
}
