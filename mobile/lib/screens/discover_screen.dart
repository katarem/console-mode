import 'dart:async';

import 'package:flutter/material.dart';

import '../services/discovery.dart';
import '../services/models.dart';

/// Busca PCs con Console Mode en la red local y devuelve el elegido como [PairTarget].
class DiscoverScreen extends StatefulWidget {
  const DiscoverScreen({super.key});

  @override
  State<DiscoverScreen> createState() => _DiscoverScreenState();
}

class _DiscoverScreenState extends State<DiscoverScreen> {
  final _found = <DiscoveredPc>[];
  StreamSubscription<DiscoveredPc>? _sub;
  bool _scanning = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _scan();
  }

  @override
  void dispose() {
    _sub?.cancel();
    super.dispose();
  }

  void _scan() {
    _sub?.cancel();
    setState(() {
      _found.clear();
      _scanning = true;
      _error = null;
    });
    _sub = Discovery.scan(timeout: const Duration(seconds: 4)).listen(
      (pc) => setState(() => _found.add(pc)),
      onError: (Object e) => setState(() => _error = '$e'),
      onDone: () {
        if (mounted) setState(() => _scanning = false);
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Buscar en la red'),
        bottom: _scanning
            ? const PreferredSize(preferredSize: Size.fromHeight(4), child: LinearProgressIndicator())
            : null,
        actions: [
          IconButton(
            tooltip: 'Buscar de nuevo',
            icon: const Icon(Icons.refresh),
            onPressed: _scanning ? null : _scan,
          ),
        ],
      ),
      body: _found.isEmpty ? _empty(context) : _list(),
    );
  }

  Widget _list() => ListView(
        padding: const EdgeInsets.all(12),
        children: [
          for (final pc in _found)
            Card(
              child: ListTile(
                leading: const CircleAvatar(child: Icon(Icons.desktop_windows_outlined)),
                title: Text(pc.name),
                subtitle: Text('${pc.host}:${pc.port}'),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => Navigator.of(context).pop(
                  PairTarget(hosts: [pc.host], port: pc.port, pcId: pc.pcId, name: pc.name),
                ),
              ),
            ),
        ],
      );

  Widget _empty(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Text(
            _scanning
                ? 'Buscando PCs con Console Mode…'
                : _error ??
                    'No se ha encontrado ningún PC.\n\nComprueba que el móvil está en la misma Wi-Fi, '
                        'que Console Mode está abierto en el PC y que el firewall de Windows permite la app '
                        '(puertos 47820 TCP y 47821 UDP). También puedes escanear el código QR.',
            textAlign: TextAlign.center,
          ),
        ),
      );
}
