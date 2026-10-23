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
                      if (connected && c.supportsQuickSwitch) ...[
                        const SizedBox(height: 24),
                        ConstrainedBox(
                          constraints: const BoxConstraints(maxWidth: 360),
                          child: _QuickSwitchCard(connection: c),
                        ),
                      ],
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

/// Cambio rápido de audio y pantalla principal: se aplica al momento y no se guarda en los modos.
class _QuickSwitchCard extends StatelessWidget {
  const _QuickSwitchCard({required this.connection});

  final PcConnection connection;

  @override
  Widget build(BuildContext context) {
    final c = connection;
    final theme = Theme.of(context);
    String currentName(List<QuickOption> options) =>
        options.where((o) => o.current).map((o) => o.name).firstOrNull ?? 'Sin datos';

    return Card(
      clipBehavior: Clip.antiAlias,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 4),
            child: Text('Cambio rápido', style: theme.textTheme.titleMedium),
          ),
          ListTile(
            leading: const Icon(Icons.volume_up_outlined),
            title: const Text('Audio'),
            subtitle: Text(currentName(c.audio), maxLines: 1, overflow: TextOverflow.ellipsis),
            trailing: const Icon(Icons.chevron_right),
            enabled: c.audio.isNotEmpty && !c.busy,
            onTap: () => _pick(context, 'Salida de audio', c.audio, c.setAudio),
          ),
          ListTile(
            leading: const Icon(Icons.monitor_outlined),
            title: const Text('Pantalla principal'),
            subtitle: Text(currentName(c.displays), maxLines: 1, overflow: TextOverflow.ellipsis),
            trailing: const Icon(Icons.chevron_right),
            enabled: c.displays.isNotEmpty && !c.busy,
            onTap: () => _pick(context, 'Pantalla principal', c.displays, c.setDisplay),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
            child: Text(
              'Temporal: no cambia la configuración de tus modos. El próximo cambio de modo aplica lo configurado en el PC.',
              style: theme.textTheme.bodySmall,
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _pick(
    BuildContext context,
    String title,
    List<QuickOption> options,
    void Function(String id) onSelected,
  ) async {
    final id = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: ListView(
          shrinkWrap: true,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(24, 0, 24, 8),
              child: Text(title, style: Theme.of(context).textTheme.titleLarge),
            ),
            for (final o in options)
              ListTile(
                leading: Icon(o.current ? Icons.radio_button_checked : Icons.radio_button_unchecked),
                title: Text(o.name),
                onTap: () => Navigator.pop(context, o.id),
              ),
          ],
        ),
      ),
    );
    if (id != null && !options.any((o) => o.id == id && o.current)) {
      onSelected(id);
    }
  }
}
