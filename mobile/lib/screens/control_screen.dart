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
          appBar: AppBar(
            title: Text(c.pc.name),
            actions: [
              IconButton(
                tooltip: 'Encendido y apagado',
                icon: const Icon(Icons.tune),
                onPressed: () => _showPowerSettings(context, c),
              ),
            ],
          ),
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
                      _PowerButton(connection: c),
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
      _ when c.waking => (Icons.power_settings_new, 'Encendiendo el PC…', scheme.tertiary),
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
        if (c.waking) ...[
          const SizedBox(height: 16),
          const SizedBox(width: 160, child: LinearProgressIndicator()),
          const SizedBox(height: 12),
          Text(
            'Se ha enviado la señal de encendido. Windows puede tardar un poco en arrancar.',
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 4),
          TextButton.icon(
            onPressed: c.cancelWake,
            icon: const Icon(Icons.close),
            label: const Text('Dejar de esperar'),
          ),
        ] else if (c.status == ConnectionStatus.offline) ...[
          const SizedBox(height: 8),
          TextButton.icon(
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
          if (c.microphones.isNotEmpty)
            ListTile(
              leading: const Icon(Icons.mic_none_outlined),
              title: const Text('Micrófono'),
              subtitle: Text(currentName(c.microphones), maxLines: 1, overflow: TextOverflow.ellipsis),
              trailing: const Icon(Icons.chevron_right),
              enabled: !c.busy,
              onTap: () => _pick(context, 'Micrófono', c.microphones, c.setMicrophone),
            ),
          if (c.communications.isNotEmpty)
            ListTile(
              leading: const Icon(Icons.headset_mic_outlined),
              title: const Text('Comunicaciones'),
              subtitle: Text(currentName(c.communications), maxLines: 1, overflow: TextOverflow.ellipsis),
              trailing: const Icon(Icons.chevron_right),
              enabled: !c.busy,
              onTap: () => _pick(
                context,
                'Comunicaciones (Discord, Teams…)',
                c.communications,
                c.setCommunications,
              ),
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

/// Ajustes de encendido y apagado de este PC: modo al encender y qué hace el botón de apagar.
Future<void> _showPowerSettings(BuildContext context, PcConnection connection) => showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (context) => ListenableBuilder(
        listenable: connection,
        builder: (context, _) {
          final theme = Theme.of(context);
          final pc = connection.pc;
          return SafeArea(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(24, 0, 24, 24),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Encendido y apagado', style: theme.textTheme.titleLarge),
                  const SizedBox(height: 20),
                  Text('Al encender el PC, ponerlo en', style: theme.textTheme.titleSmall),
                  const SizedBox(height: 8),
                  SizedBox(
                    width: double.infinity,
                    child: SegmentedButton<WakeMode>(
                      segments: const [
                        ButtonSegment(value: WakeMode.console, icon: Icon(Icons.sports_esports), label: Text('Consola')),
                        ButtonSegment(value: WakeMode.pc, icon: Icon(Icons.desktop_windows_outlined), label: Text('PC')),
                        ButtonSegment(value: WakeMode.keep, label: Text('Sin cambiar')),
                      ],
                      selected: {pc.wakeMode},
                      onSelectionChanged: (v) => connection.updatePowerSettings(wakeMode: v.first),
                    ),
                  ),
                  const SizedBox(height: 20),
                  Text('El botón de apagar', style: theme.textTheme.titleSmall),
                  const SizedBox(height: 8),
                  SizedBox(
                    width: double.infinity,
                    child: SegmentedButton<PowerOffAction>(
                      segments: const [
                        ButtonSegment(value: PowerOffAction.sleep, icon: Icon(Icons.bedtime_outlined), label: Text('Suspende')),
                        ButtonSegment(value: PowerOffAction.shutdown, icon: Icon(Icons.power_settings_new), label: Text('Apaga')),
                      ],
                      selected: {pc.powerOff},
                      onSelectionChanged: (v) => connection.updatePowerSettings(powerOff: v.first),
                    ),
                  ),
                  const SizedBox(height: 12),
                  Text(
                    'Suspender es más rápido y el PC se vuelve a encender desde aquí con más fiabilidad que desde apagado.',
                    style: theme.textTheme.bodySmall,
                  ),
                  const SizedBox(height: 16),
                  const Divider(),
                  TextButton.icon(
                    onPressed: () => _forgetPc(context, connection),
                    icon: const Icon(Icons.delete_outline),
                    label: const Text('Olvidar este PC'),
                    style: TextButton.styleFrom(foregroundColor: theme.colorScheme.error),
                  ),
                ],
              ),
            ),
          );
        },
      ),
    );

/// Quita el PC de la lista del móvil (tras confirmar) y vuelve a la lista.
Future<void> _forgetPc(BuildContext context, PcConnection connection) async {
  final navigator = Navigator.of(context);
  final ok = await showDialog<bool>(
    context: context,
    builder: (context) => AlertDialog(
      title: Text('¿Olvidar «${connection.pc.name}»?'),
      content: const Text('Tendrás que volver a emparejarlo. Para revocar también el acceso en el PC, '
          'quítalo de la lista de dispositivos de confianza en Console Mode.'),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancelar')),
        FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Olvidar')),
      ],
    ),
  );
  if (ok != true) return;
  connection.stop();
  await connection.forget();
  navigator
    ..pop() // hoja de ajustes
    ..pop(); // pantalla del PC
}

/// Botón de encendido: enciende el PC (Wake-on-LAN) si no está conectado y lo suspende o apaga si lo está.
class _PowerButton extends StatelessWidget {
  const _PowerButton({required this.connection});

  final PcConnection connection;

  @override
  Widget build(BuildContext context) {
    final c = connection;
    final theme = Theme.of(context);
    final connected = c.status == ConnectionStatus.connected;

    final String label;
    final Color color;
    final VoidCallback? onPressed;
    String? hint;
    if (c.waking) {
      // Sigue activo: si el PC se ha reiniciado a mano o no ha llegado a Windows, se puede volver a encender.
      (label, color, onPressed) = ('Encendiendo… · pulsa para reenviar', theme.colorScheme.tertiary, c.wake);
    } else if (c.poweringOff) {
      (label, color, onPressed) = ('${c.pc.powerOff.progress}…', theme.colorScheme.outline, null);
    } else if (connected) {
      (label, color, onPressed) = (
        c.pc.powerOff.label,
        Colors.redAccent,
        !c.supportsPower || c.busy ? null : () => _confirmPowerOff(context),
      );
      if (!c.supportsPower) hint = 'Para apagarlo desde aquí, actualiza Console Mode en el PC.';
    } else {
      // Sin conexión, o mientras se intenta conectar: se puede encender ya, sin esperar a saber si responde (si ya
      // estaba encendido, la señal no le afecta y al conectar se pone en el modo elegido).
      final mode = c.pc.wakeMode.mode;
      (label, color, onPressed) = (
        switch (mode) {
          PcMode.console => 'Encender en modo consola',
          PcMode.pc => 'Encender en modo PC',
          null => 'Encender',
        },
        Colors.green,
        c.pc.canWake && c.status != ConnectionStatus.unauthorized ? c.wake : null,
      );
      if (!c.pc.canWake) {
        hint = 'Para encenderlo desde aquí, conecta una vez con el PC encendido y Console Mode 1.5.0 o posterior.';
      }
    }

    final enabled = onPressed != null;
    return Column(
      children: [
        SizedBox(
          width: 88,
          height: 88,
          child: FilledButton(
            onPressed: onPressed,
            style: FilledButton.styleFrom(
              shape: const CircleBorder(),
              padding: EdgeInsets.zero,
              backgroundColor: color,
              foregroundColor: Colors.white,
              disabledBackgroundColor: color.withValues(alpha: 0.35),
              disabledForegroundColor: Colors.white70,
            ),
            child: const Icon(Icons.power_settings_new, size: 44),
          ),
        ),
        const SizedBox(height: 10),
        Text(label, style: theme.textTheme.titleSmall?.copyWith(color: enabled ? null : theme.disabledColor)),
        if (hint != null) ...[
          const SizedBox(height: 6),
          ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 300),
            child: Text(hint, textAlign: TextAlign.center, style: theme.textTheme.bodySmall),
          ),
        ],
      ],
    );
  }

  Future<void> _confirmPowerOff(BuildContext context) async {
    final action = connection.pc.powerOff;
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('¿${action.label} «${connection.pc.name}»?'),
        content: Text(action == PowerOffAction.sleep
            ? 'El PC se suspenderá. Puedes volver a encenderlo desde aquí.'
            : 'El PC se apagará del todo. Para encenderlo desde aquí hace falta Wake-on-LAN desde apagado.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancelar')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: Text(action.label)),
        ],
      ),
    );
    if (ok == true) connection.powerOff();
  }
}
