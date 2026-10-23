import 'package:flutter/material.dart';

import '../services/models.dart';
import '../services/storage.dart';
import 'control_screen.dart';
import 'discover_screen.dart';
import 'pairing_screen.dart';
import 'scan_screen.dart';

/// Lista de PCs emparejados y acceso a añadir uno nuevo (QR o búsqueda en red).
class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key, required this.storage});

  final AppStorage storage;

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  AppStorage get storage => widget.storage;

  @override
  Widget build(BuildContext context) {
    final pcs = storage.pcs;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Console Mode'),
        actions: [
          IconButton(
            tooltip: 'Nombre de este móvil',
            icon: const Icon(Icons.badge_outlined),
            onPressed: _editDeviceName,
          ),
        ],
      ),
      body: pcs.isEmpty ? _empty(context) : _list(pcs),
      floatingActionButton: pcs.isEmpty
          ? null
          : FloatingActionButton.extended(
              onPressed: _addPc,
              icon: const Icon(Icons.add),
              label: const Text('Añadir PC'),
            ),
    );
  }

  Widget _empty(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.sports_esports_outlined, size: 72, color: theme.colorScheme.primary),
            const SizedBox(height: 16),
            Text('Vincula tu PC', style: theme.textTheme.headlineSmall),
            const SizedBox(height: 8),
            Text(
              'Abre Console Mode en el PC, ve a la pestaña «Móviles» y escanea el código QR, '
              'o búscalo en tu red Wi-Fi.',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium,
            ),
            const SizedBox(height: 24),
            FilledButton.icon(
              onPressed: () => _addWith(_AddMethod.qr),
              icon: const Icon(Icons.qr_code_scanner),
              label: const Text('Escanear código QR'),
            ),
            const SizedBox(height: 12),
            OutlinedButton.icon(
              onPressed: () => _addWith(_AddMethod.network),
              icon: const Icon(Icons.wifi_find),
              label: const Text('Buscar en la red'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _list(List<SavedPc> pcs) {
    return ListView(
      padding: const EdgeInsets.fromLTRB(12, 12, 12, 96),
      children: [
        for (final pc in pcs)
          Card(
            child: ListTile(
              leading: const CircleAvatar(child: Icon(Icons.desktop_windows_outlined)),
              title: Text(pc.name),
              subtitle: Text('${pc.hosts.first}:${pc.port}'),
              trailing: PopupMenuButton<String>(
                onSelected: (v) {
                  if (v == 'remove') _removePc(pc);
                },
                itemBuilder: (_) => const [
                  PopupMenuItem(value: 'remove', child: Text('Olvidar este PC')),
                ],
              ),
              onTap: () => _open(pc),
            ),
          ),
      ],
    );
  }

  Future<void> _open(SavedPc pc) async {
    await Navigator.of(context).push(
      MaterialPageRoute(builder: (_) => ControlScreen(pc: pc, storage: storage)),
    );
    if (mounted) setState(() {});
  }

  Future<void> _addPc() async {
    final method = await showModalBottomSheet<_AddMethod>(
      context: context,
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.qr_code_scanner),
              title: const Text('Escanear código QR'),
              onTap: () => Navigator.pop(context, _AddMethod.qr),
            ),
            ListTile(
              leading: const Icon(Icons.wifi_find),
              title: const Text('Buscar en la red'),
              onTap: () => Navigator.pop(context, _AddMethod.network),
            ),
          ],
        ),
      ),
    );
    if (method != null) await _addWith(method);
  }

  Future<void> _addWith(_AddMethod method) async {
    final navigator = Navigator.of(context);
    final target = await navigator.push<PairTarget>(MaterialPageRoute(
      builder: (_) => method == _AddMethod.qr ? const ScanScreen() : const DiscoverScreen(),
    ));
    if (target == null || !mounted) return;

    final pc = await navigator.push<SavedPc>(MaterialPageRoute(
      builder: (_) => PairingScreen(target: target, storage: storage),
    ));
    if (!mounted) return;
    setState(() {});
    if (pc != null) await _open(pc);
  }

  Future<void> _removePc(SavedPc pc) async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: Text('¿Olvidar «${pc.name}»?'),
        content: const Text(
            'Tendrás que volver a emparejarlo. Para revocar también el acceso en el PC, '
            'quítalo de la lista de dispositivos de confianza en Console Mode.'),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancelar')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Olvidar')),
        ],
      ),
    );
    if (ok == true) {
      await storage.removePc(pc.id);
      if (mounted) setState(() {});
    }
  }

  Future<void> _editDeviceName() async {
    final controller = TextEditingController(text: storage.deviceName);
    final name = await showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Nombre de este móvil'),
        content: TextField(
          controller: controller,
          autofocus: true,
          maxLength: 64,
          decoration: const InputDecoration(helperText: 'Es el nombre que verás en el PC.'),
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancelar')),
          FilledButton(onPressed: () => Navigator.pop(context, controller.text), child: const Text('Guardar')),
        ],
      ),
    );
    if (name != null) {
      await storage.setDeviceName(name);
      if (mounted) setState(() {});
    }
  }
}

enum _AddMethod { qr, network }
